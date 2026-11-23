//! tviburi-afcheck TABLE.tsv GPIO-MODES.xml ALIASES.tsv RULED.tsv (TVI-WO-1301 (1); RUL-14).
//!
//! `model/ds12117-rev10-table9.tsv` (DS12117 Rev 10 Table 9, the GPIO guard's authority) against ST's open pin data
//! (the GPIO IP modes file `STM32H753ZITx.xml` names). Per pin and AF, DS's set is the cell split at `/` (`-` is
//! empty); ST's is every `PinSignal` of that pin whose `GPIO_AF` value is `GPIO_AF<n>_*`. A pair that differs is a
//! disagreement, classed and printed with both sources:
//!   af-number  a signal named by both, on this pin, at different AF numbers: never ruled, always a failure;
//!   presence   one source has no function here and the other has one;
//!   alias      every difference is explained by ALIASES.tsv (ruled D4);
//!   name       the rest.
//! Exit 0 only if every presence and name disagreement is a row of RULED.tsv, every RULED row still disagrees
//! with the class it was ruled under, and nothing is af-number. Exit 1 otherwise, naming each failure; 2 on bad input.
use std::collections::{BTreeMap, BTreeSet};
use std::process::ExitCode;

type Set = BTreeSet<String>;

pub struct Row {
    pub class: &'static str,
    pub pin: String,
    pub af: u8,
    pub ds: String,
    pub st: String,
    pub note: String,
}

fn pin_key(p: &str) -> (char, u32) {
    (p.chars().nth(1).unwrap_or('?'), p[2..].parse().unwrap_or(0))
}

/// The table: (pin, AF) -> the cell as printed.
pub fn read_table(text: &str) -> Result<BTreeMap<(String, u8), String>, String> {
    let mut t = BTreeMap::new();
    for (i, line) in text.lines().enumerate().skip(1) {
        let f: Vec<&str> = line.split('\t').collect();
        if f.len() != 5 {
            return Err(format!("table line {}: {} fields, not 5", i + 1, f.len()));
        }
        let af: u8 = f[2].parse().map_err(|_| format!("table line {}: AF {:?}", i + 1, f[2]))?;
        if af > 15 || t.insert((f[1].to_string(), af), f[3].to_string()).is_some() {
            return Err(format!("table line {}: AF out of range or repeated", i + 1));
        }
    }
    Ok(t)
}

/// ST's modes file: (pin, AF) -> signals. Only `GPIO_Pin` / `PinSignal` / `SpecificParameter Name="GPIO_AF"` /
/// `PossibleValue` are read; a pin's name is its first token (`PA13 (JTMS/SWDIO)` is PA13, `PC14-OSC32_IN` is PC14).
pub fn read_st(xml: &str) -> Result<BTreeMap<(String, u8), Set>, String> {
    let attr = |tag: &str, name: &str| -> Option<String> {
        let k = format!(" {}=\"", name);
        let i = tag.find(&k)? + k.len();
        Some(tag[i..i + tag[i..].find('"')?].to_string())
    };
    let mut st: BTreeMap<(String, u8), Set> = BTreeMap::new();
    let (mut pin, mut signal, mut in_af) = (None::<String>, None::<String>, false);
    let mut rest = xml;
    while let Some(lt) = rest.find('<') {
        let gt = rest[lt..].find('>').ok_or("unterminated tag")? + lt;
        let tag = &rest[lt + 1..gt];
        let after = &rest[gt + 1..];
        let name = tag.split(|c: char| c.is_whitespace() || c == '/').next().unwrap_or("");
        match name {
            "GPIO_Pin" => pin = attr(tag, "Name").map(|n| n.split([' ', '-']).next().unwrap_or("").to_string()),
            "/GPIO_Pin" => pin = None,
            "PinSignal" => signal = attr(tag, "Name"),
            "/PinSignal" => signal = None,
            "SpecificParameter" => in_af = attr(tag, "Name").as_deref() == Some("GPIO_AF"),
            "/SpecificParameter" => in_af = false,
            "PossibleValue" if in_af => {
                if let (Some(p), Some(s)) = (&pin, &signal) {
                    let v = after[..after.find('<').ok_or("unterminated value")?].trim();
                    let n = v.strip_prefix("GPIO_AF").and_then(|x| x.split('_').next()).and_then(|x| x.parse::<u8>().ok());
                    let n = n.filter(|n| *n <= 15).ok_or_else(|| format!("{}: GPIO_AF value {:?}", p, v))?;
                    st.entry((p.clone(), n)).or_default().insert(s.clone());
                }
            }
            _ => {}
        }
        rest = after;
    }
    Ok(st)
}

/// The alias map: DS signal -> ST signals.
pub fn read_aliases(text: &str) -> Result<BTreeMap<String, Set>, String> {
    let mut a = BTreeMap::new();
    for line in text.lines().filter(|l| !l.starts_with('#') && !l.starts_with("ds_signal\t")) {
        let f: Vec<&str> = line.split('\t').collect();
        if f.len() != 3 || f[2].trim().is_empty() {
            return Err(format!("alias line {:?}: needs ds_signal, st_signals and a rule", line));
        }
        a.insert(f[0].to_string(), f[1].split('/').map(String::from).collect());
    }
    Ok(a)
}

/// The ruled list: (class, pin, AF), each with its ruling.
pub fn read_ruled(text: &str) -> Result<BTreeMap<(String, String, u8), String>, String> {
    let mut r = BTreeMap::new();
    for line in text.lines().filter(|l| !l.starts_with('#') && !l.starts_with("class\t")) {
        let f: Vec<&str> = line.split('\t').collect();
        if f.len() != 6 || f[5].trim().is_empty() {
            return Err(format!("ruled line {:?}: needs six fields, the last a ruling", line));
        }
        let af = f[2].parse().map_err(|_| format!("ruled line {:?}: AF", line))?;
        r.insert((f[0].to_string(), f[1].to_string(), af), f[5].to_string());
    }
    Ok(r)
}

pub fn compare(table: &BTreeMap<(String, u8), String>, st: &BTreeMap<(String, u8), Set>, aliases: &BTreeMap<String, Set>) -> Vec<Row> {
    let split = |c: &str| -> Set { if c == "-" { Set::new() } else { c.split('/').map(String::from).collect() } };
    let mut pins: Vec<&String> = table.keys().map(|(p, _)| p).collect::<BTreeSet<_>>().into_iter().collect();
    pins.sort_by_key(|p| pin_key(p));
    let empty = Set::new();
    let mut out = Vec::new();
    for pin in pins {
        for af in 0..16u8 {
            let cell = &table[&(pin.clone(), af)];
            let d = split(cell);
            let s = st.get(&(pin.clone(), af)).unwrap_or(&empty);
            if &d == s {
                continue;
            }
            let mut elsewhere = Vec::new();
            for x in s.difference(&d) {
                let o: Vec<u8> = (0..16).filter(|a| *a != af && split(&table[&(pin.clone(), *a)]).contains(x)).collect();
                if !o.is_empty() { elsewhere.push(format!("ST {} is DS AF{:?}", x, o)); }
            }
            for x in d.difference(s) {
                let o: Vec<u8> = (0..16).filter(|a| *a != af && st.get(&(pin.clone(), *a)).is_some_and(|v| v.contains(x))).collect();
                if !o.is_empty() { elsewhere.push(format!("DS {} is ST AF{:?}", x, o)); }
            }
            let mapped: Set = d.iter().flat_map(|x| aliases.get(x).cloned().unwrap_or_else(|| [x.clone()].into())).collect();
            let class = if !elsewhere.is_empty() { "af-number" }
                else if d.is_empty() || s.is_empty() { "presence" }
                else if &mapped == s { "alias" } else { "name" };
            out.push(Row { class, pin: pin.clone(), af, ds: cell.clone(), st: s.iter().cloned().collect::<Vec<_>>().join("/"), note: elsewhere.join("; ") });
        }
    }
    out
}

/// The verdict: every failure, by line. Empty is a pass.
pub fn verdict(rows: &[Row], ruled: &BTreeMap<(String, String, u8), String>) -> Vec<String> {
    let mut fails = Vec::new();
    let mut seen = BTreeSet::new();
    for r in rows {
        let key = (r.class.to_string(), r.pin.clone(), r.af);
        match r.class {
            "af-number" => fails.push(format!("FAIL af-number {} AF{}: DS {:?}, ST {:?} ({})", r.pin, r.af, r.ds, r.st, r.note)),
            "alias" => {}
            _ if ruled.contains_key(&key) => { seen.insert(key); }
            _ => fails.push(format!("FAIL unruled {} {} AF{}: DS {:?}, ST {:?}", r.class, r.pin, r.af, r.ds, r.st)),
        }
    }
    for (k, why) in ruled {
        if !seen.contains(k) {
            fails.push(format!("FAIL stale ruling {} {} AF{} ({}): it no longer disagrees as ruled", k.0, k.1, k.2, why));
        }
    }
    fails
}

fn main() -> ExitCode {
    let a: Vec<String> = std::env::args().collect();
    if a.len() != 5 {
        eprintln!("usage: tviburi-afcheck TABLE.tsv GPIO-MODES.xml ALIASES.tsv RULED.tsv");
        return ExitCode::from(2);
    }
    let read = |p: &str| std::fs::read_to_string(p).map_err(|e| format!("{}: {}", p, e));
    let run = || -> Result<(Vec<Row>, Vec<String>), String> {
        let table = read_table(&read(&a[1])?)?;
        if table.len() != 2688 {
            return Err(format!("{}: {} cells, not 168 pins x 16", a[1], table.len()));
        }
        let rows = compare(&table, &read_st(&read(&a[2])?)?, &read_aliases(&read(&a[3])?)?);
        let fails = verdict(&rows, &read_ruled(&read(&a[4])?)?);
        Ok((rows, fails))
    };
    match run() {
        Err(e) => { eprintln!("tviburi-afcheck: {}", e); ExitCode::from(2) }
        Ok((rows, fails)) => {
            let mut n: BTreeMap<&str, usize> = BTreeMap::new();
            for r in &rows { *n.entry(r.class).or_default() += 1; }
            println!("disagreements: {}", n.iter().map(|(k, v)| format!("{} {}", k, v)).collect::<Vec<_>>().join(", "));
            for f in &fails { println!("{}", f); }
            if fails.is_empty() { println!("PASS: every disagreement is ruled"); ExitCode::SUCCESS } else { ExitCode::from(1) }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    const ROOT: &str = concat!(env!("CARGO_MANIFEST_DIR"), "/../..");
    fn load() -> (BTreeMap<(String, u8), String>, BTreeMap<(String, u8), Set>, BTreeMap<String, Set>, BTreeMap<(String, String, u8), String>) {
        let r = |p: &str| std::fs::read_to_string(format!("{}/{}", ROOT, p)).unwrap();
        (read_table(&r("model/ds12117-rev10-table9.tsv")).unwrap(),
         read_st(&r("tests/1301/fixtures/st-open-pin-data/GPIO-STM32H747_gpio_v1_0_Modes.xml")).unwrap(),
         read_aliases(&r("tests/1301/st-aliases.tsv")).unwrap(),
         read_ruled(&r("tests/1301/st-ruled.tsv")).unwrap())
    }

    #[test]
    fn the_committed_table_passes_with_every_disagreement_ruled() {
        let (t, s, a, r) = load();
        let rows = compare(&t, &s, &a);
        assert_eq!(rows.iter().filter(|r| r.class == "af-number").count(), 0);
        assert_eq!(rows.iter().filter(|r| r.class == "alias").count(), 189);
        assert_eq!(rows.len(), 189 + r.len());
        assert_eq!(verdict(&rows, &r), Vec::<String>::new());
    }

    #[test]
    fn a_cell_blanked_in_the_table_fails_as_unruled() {
        let (mut t, s, a, r) = load();
        t.insert(("PA5".into(), 5), "-".into()); // SPI1_SCK/I2S1_CK, p89
        let f = verdict(&compare(&t, &s, &a), &r);
        assert!(f.iter().any(|x| x.contains("unruled presence PA5 AF5")), "{:?}", f);
    }

    #[test]
    fn a_signal_moved_to_another_af_fails_as_af_number() {
        let (mut t, s, a, r) = load();
        t.insert(("PA5".into(), 5), "-".into());
        t.insert(("PA5".into(), 6), "SPI1_SCK/I2S1_CK".into());
        let f = verdict(&compare(&t, &s, &a), &r);
        assert!(f.iter().any(|x| x.starts_with("FAIL af-number PA5 AF6")), "{:?}", f);
    }

    #[test]
    fn a_ruled_row_that_no_longer_disagrees_fails_as_stale() {
        let (t, mut s, a, r) = load();
        s.entry(("PA0".into(), 15)).or_default().insert("EVENTOUT".into()); // D1's first row, now agreeing
        let f = verdict(&compare(&t, &s, &a), &r);
        assert!(f.iter().any(|x| x.contains("stale ruling presence PA0 AF15")), "{:?}", f);
    }

    #[test]
    fn st_pins_read_by_their_first_token() {
        let s = read_st(r#"<GPIO_Pin PortName="PA" Name="PA13 (JTMS/SWDIO)"><PinSignal Name="X"><SpecificParameter Name="GPIO_AF"><PossibleValue>GPIO_AF3_Y</PossibleValue></SpecificParameter></PinSignal></GPIO_Pin><GPIO_Pin Name="PC14-OSC32_IN (OSC32_IN)"><PinSignal Name="Z"><SpecificParameter Name="GPIO_Mode"><PossibleValue>GPIO_AF9_W</PossibleValue></SpecificParameter></PinSignal></GPIO_Pin>"#).unwrap();
        assert_eq!(s.get(&("PA13".into(), 3)).map(|v| v.len()), Some(1));
        assert!(s.keys().all(|(p, _)| p != "PC14"), "a non-GPIO_AF parameter must not count");
    }
}
