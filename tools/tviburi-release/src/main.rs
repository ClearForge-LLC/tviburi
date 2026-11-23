//! tviburi-release — Tviburi's release program (RUL-09, RUL-10, RUL-03; TVI-WO-1200). No crate dependency.
//!
//!   tviburi-release build <repo> <renode-root> <out-dir>
//!       From one commit's tree, writes into the new directory <out-dir>:
//!         Tviburi.cs                 the overlay as one C# file (overlay.rs): the body, then the template filled
//!                                    with the release hash;
//!         TviburiRelease.cs.template the release constant's template;
//!         MANIFEST                   the build strings, the stock pins, the class map, the sha256 of every source
//!                                    and released file, and the two excluded files with how each is checked.
//!                                    The release hash is the sha256 of its bytes;
//!         LEDGER.jsonl               the gap ledger, whose first line names the release hash;
//!         tviburi-release.tar        those four files, with fixed order and metadata.
//!       Prints `release sha256:<hex>` and `ledger sha256:<hex>`. Two builds of one tree are byte-identical.
//!   tviburi-release verify <dir> <pinned release hash>
//!       The consumer's binding check (RUL-10 (f)) as bytes, with no C# parsing: MANIFEST is the pinned hash; each
//!       listed file matches; Tviburi.cs is exactly the listed body followed by the template filled with the pinned
//!       hash; LEDGER.jsonl reads strictly for that hash; the tarball, if present, holds exactly those files.
//!       Exit 0 on VERIFY PASS, 1 on VERIFY FAIL, 2 on a usage error.
//!   tviburi-release cases <LEDGER.jsonl> <guard type> <base address> <region size>
//!       A Renode monitor script with one write per case, generated from the ledger's field rows of one guard
//!       (gate clause 6): the register's baseline, each allowed value other than it, and one value each field
//!       does not allow; and a write to every 4-aligned offset of the region that holds no register. Each case
//!       is a `# CASE <pass|refuse> <register>.<field> <offset> <value>` line, the write, and a machine reset.

#[path = "../../tviburi-check/src/json.rs"]
mod json;
mod fields;
mod ledger;
mod overlay;
mod sha256;
mod stock;
mod tar;

use json::Value;
use ledger::{n, s, Row};
use overlay::Source;
use std::collections::BTreeMap;
use std::fs;
use std::path::Path;
use std::process::ExitCode;

const OVERLAY: &str = "Tviburi.cs";
const TEMPLATE: &str = "TviburiRelease.cs.template";
const MANIFEST: &str = "MANIFEST";
const LEDGER: &str = "LEDGER.jsonl";
const TARBALL: &str = "tviburi-release.tar";
const TOOL: &str = "tools/tviburi-release";
const BENCH: &str = "model/bench-checks.tsv";
const MANIFEST_FORMAT: &str = "tviburi-release-manifest 1";

struct Pins {
    builds: Vec<(String, String)>,
    stock: Vec<stock::Pin>,
    /// (stock type, guard type)
    classes: Vec<(String, String)>,
}

/// Tab-separated rows of `cols` non-empty fields; `#` lines are comments.
fn tsv<'a>(path: &str, text: &'a str, cols: usize) -> Result<Vec<Vec<&'a str>>, String> {
    let mut out = Vec::new();
    for (i, line) in text.lines().enumerate() {
        if line.starts_with('#') {
            continue;
        }
        let f: Vec<&str> = line.split('\t').collect();
        if f.len() != cols || f.iter().any(|x| x.is_empty() || x.trim() != *x) {
            return Err(format!("{path}:{}: not {cols} non-empty tab-separated fields", i + 1));
        }
        out.push(f);
    }
    Ok(out)
}

fn pins(repo: &Path) -> Result<Pins, String> {
    let path = format!("{TOOL}/pins.tsv");
    let src = Source { path: path.clone(), bytes: fs::read(repo.join(&path)).map_err(|e| format!("{path}: {e}"))? };
    let mut p = Pins { builds: vec![], stock: vec![], classes: vec![] };
    for f in tsv(&path, overlay::text(&src)?, 3)? {
        match f[0] {
            "build" => p.builds.push((f[1].into(), f[2].into())),
            "stock" if f[1].len() == 64 && f[1].bytes().all(|b| b.is_ascii_hexdigit() && !b.is_ascii_uppercase()) => {
                p.stock.push(stock::Pin { sha256: f[1].into(), path: f[2].into() })
            }
            "class" => {
                if p.classes.iter().any(|c| c.0 == f[1] || c.1 == f[2]) {
                    return Err(format!("{path}: the class map names {} or {} twice", f[1], f[2]));
                }
                p.classes.push((f[1].into(), f[2].into()));
            }
            _ => return Err(format!("{path}: a row this release cannot read: {}", f.join(" "))),
        }
    }
    let names: Vec<&str> = p.builds.iter().map(|b| b.0.as_str()).collect();
    if names != ["renode", "infrastructure"] || p.stock.is_empty() || p.classes.is_empty() {
        return Err(format!("{path}: needs `build renode`, then `build infrastructure`, a `stock` row and a `class` row"));
    }
    Ok(p)
}

/// RUL-16's state for a class string of a guard's table, and the class's own detail.
fn state(class: &str) -> Option<(&'static str, Option<String>)> {
    Some(match class {
        "modeled by the guard" => ("guard", None),
        "inherited, unaudited (acknowledged by name)" => ("inherited-unaudited", None),
        "acknowledged by name" => ("acknowledged", None),
        "tag" => ("tag", None),
        "reserved" => ("reserved", None),
        "read-only" => ("read-only", None),
        "undefined in the stock model" => ("stock-undefined", None),
        c => ("stock-wrong", Some(c.strip_prefix("stock, wrong (")?.strip_suffix(')')?.to_string())),
    })
}

/// The TVI-WO-0100b.rm0433.md §4 acknowledgement a `behaves as` text cites, as `(A<n>)` or `(A<n>; …)`.
fn acknowledgement(behaves: &str) -> Option<String> {
    let i = behaves.find("(A")?;
    let digits: String = behaves[i + 2..].chars().take_while(char::is_ascii_digit).collect();
    let after = behaves[i + 2 + digits.len()..].chars().next();
    (!digits.is_empty() && matches!(after, Some(')') | Some(';'))).then(|| format!("TVI-WO-0100b.rm0433.md §4 A{digits}"))
}

/// One ledger row per field of a guard's table, after checking that every bit 31:0 of every register is in
/// exactly one row.
fn field_rows(guard: &str, path: &str, rows: &[fields::Row]) -> Result<Vec<Row>, String> {
    // Keyed by offset, what the bus decodes: one register name per offset, one offset per name.
    let mut cover: BTreeMap<u32, (&str, u64)> = BTreeMap::new();
    for r in rows {
        let mask = ((1u64 << (r.msb + 1)) - 1) & !((1u64 << r.lsb) - 1);
        if cover.iter().any(|(o, (n, _))| *n == r.register && *o != r.offset) {
            return Err(format!("{path}: {} at two offsets", r.register));
        }
        let entry = cover.entry(r.offset).or_insert((&r.register, 0));
        if entry.0 != r.register {
            return Err(format!("{path}: {} and {} at one offset, {:#04x}", entry.0, r.register, r.offset));
        }
        if entry.1 & mask != 0 {
            return Err(format!("{path}: {}.{} covers a bit another row of {} covers", r.register, r.name, r.register));
        }
        entry.1 |= mask;
    }
    if let Some((_, (reg, m))) = cover.iter().find(|(_, (_, m))| *m != 0xFFFF_FFFF) {
        return Err(format!("{path}: bits of {reg} in no row (mask {m:#010x}): a field the ledger would not list"));
    }
    rows.iter()
        .map(|r| {
            let at = format!("{path}: {}.{}", r.register, r.name);
            let (st, detail) = state(&r.class).ok_or(format!("{at}: the class {:?} has no RUL-16 state: a stop (TVI-WO-1200 §7)", r.class))?;
            let reason = acknowledgement(&r.behaves_as);
            if st == "acknowledged" && reason.is_none() {
                return Err(format!("{at}: acknowledged with no (A<n>) reason"));
            }
            let mut m = Row::new();
            m.insert("row".into(), s("field"));
            m.insert("peripheral".into(), s(guard));
            m.insert("register".into(), s(&r.register));
            m.insert("field".into(), s(&r.name));
            m.insert("offset".into(), s(&format!("0x{:02X}", r.offset)));
            m.insert("bits".into(), s(&if r.msb == r.lsb { r.lsb.to_string() } else { format!("{}:{}", r.msb, r.lsb) }));
            m.insert("state".into(), s(st));
            m.insert("allowed".into(), r.allowed.as_ref().map_or(s("any"), |v| Value::Array(v.iter().map(|&x| n(x)).collect())));
            m.insert("behaves_as".into(), s(&r.behaves_as));
            m.insert("citation".into(), s(&format!("RM0433 Rev 9 {} p{}", r.section, r.page)));
            if let Some(d) = detail {
                m.insert("detail".into(), s(&d));
            }
            if let Some(a) = reason {
                m.insert("reason".into(), s(&a));
            }
            Ok(m)
        })
        .collect()
}

fn stock_rows(chain: &stock::Chain, pins: &Pins) -> Vec<Row> {
    // A stock file is named with its pinned sha256 (RUL-16: the rows are keyed by the pinned files' hashes).
    let pinned = |f: &str| format!("{f} sha256:{}", pins.stock.iter().find(|p| p.path == f).map_or("", |p| p.sha256.as_str()));
    let mut models: BTreeMap<&str, (Vec<&str>, Vec<&str>)> = BTreeMap::new();
    for (ty, inst, file) in &chain.declared {
        let e = models.entry(ty).or_default();
        e.0.push(inst);
        e.1.push(file.as_str());
    }
    let list = |mut v: Vec<String>| {
        v.sort();
        v.dedup();
        Value::Array(v.iter().map(|x| s(x)).collect())
    };
    let mut rows = Vec::new();
    for (ty, (insts, files)) in models {
        let mut m = Row::new();
        m.insert("row".into(), s("model"));
        m.insert("peripheral".into(), s(ty));
        m.insert("register".into(), s(""));
        m.insert("field".into(), s(""));
        m.insert("state".into(), s("inherited-unaudited"));
        m.insert("instances".into(), list(insts.into_iter().map(String::from).collect()));
        m.insert("files".into(), list(files.into_iter().map(pinned).collect()));
        if let Some((_, guard)) = pins.classes.iter().find(|c| c.0 == ty) {
            m.insert("guarded_by".into(), s(guard));
        }
        rows.push(m);
    }
    for (name, range, file) in &chain.tags {
        let mut m = Row::new();
        m.insert("row".into(), s("tag"));
        m.insert("peripheral".into(), s("Tag"));
        m.insert("register".into(), s(name));
        m.insert("field".into(), s(""));
        m.insert("state".into(), s("tag"));
        m.insert("range".into(), s(range));
        m.insert("file".into(), s(&pinned(file)));
        m.insert("behaves_as".into(), s("reads 0; a write is discarded (a stock Renode tag; TVI-WO-0100, architecture §2.0a)"));
        rows.push(m);
    }
    for (inst, file, ty, what) in &chain.canned {
        let mut m = Row::new();
        m.insert("row".into(), s("canned"));
        m.insert("peripheral".into(), s(ty));
        m.insert("register".into(), s(inst));
        m.insert("field".into(), s(""));
        m.insert("state".into(), s("canned"));
        m.insert("file".into(), s(&pinned(file)));
        m.insert("behaves_as".into(), s(what));
        rows.push(m);
    }
    rows
}

fn bench_rows(src: &Source, pins: &Pins) -> Result<Vec<Row>, String> {
    let mut rows = Vec::new();
    for f in tsv(&src.path, overlay::text(src)?, 9)? {
        if !pins.classes.iter().any(|c| c.1 == f[0]) {
            return Err(format!("{}: {} is not a guard of the class map", src.path, f[0]));
        }
        if f[8] != "not run" {
            return Err(format!("{}: {}: a result other than \"not run\" needs its own ruling", src.path, f[1]));
        }
        let mut m = Row::new();
        m.insert("row".into(), s("bench"));
        m.insert("peripheral".into(), s(f[0]));
        m.insert("register".into(), s("bench"));
        for (k, v) in ["field", "rule", "bit", "procedure", "confirms", "refutes", "citation", "result"].iter().zip(&f[1..]) {
            m.insert((*k).into(), s(v));
        }
        rows.push(m);
    }
    Ok(rows)
}

fn build(repo: &Path, renode: &Path, out: &Path) -> Result<(String, String), String> {
    let pins = pins(repo)?;
    let model_files = overlay::regular_files(repo, "model")?;
    let models = overlay::models(&model_files)?;
    let template_path = format!("{TOOL}/{TEMPLATE}");
    let template = Source { path: template_path.clone(), bytes: fs::read(repo.join(&template_path)).map_err(|e| format!("{template_path}: {e}"))? };
    overlay::check_template(&template)?;
    let body = overlay::body(&models)?;

    // The class map: each guard declared in exactly one model file, with a table the ledger can read.
    let mut rows = Vec::new();
    for (_, guard) in &pins.classes {
        let class = guard.strip_prefix("Tviburi.").ok_or(format!("{guard}: a guard type outside the Tviburi namespace"))?;
        let decl = |t: &str| t.contains(&format!("class {class} ")) || t.contains(&format!("class {class}:"));
        let homes: Vec<&&Source> = models.iter().filter(|m| overlay::text(m).is_ok_and(decl)).collect();
        let [home] = homes.as_slice() else { return Err(format!("{guard}: declared in {} model files, not one", homes.len())) };
        let t = overlay::text(home)?;
        if !t.contains("namespace Antmicro.Renode.Peripherals.Tviburi\n") || !fields::has_table(t) {
            return Err(format!("{}: {guard} is not in namespace Antmicro.Renode.Peripherals.Tviburi with a Fields table this release reads", home.path));
        }
        rows.extend(field_rows(guard, &home.path, &fields::read(&home.path, t)?)?);
    }
    let chain = stock::read(renode, &pins.stock)?;
    rows.extend(stock_rows(&chain, &pins));
    let bench = model_files.iter().find(|f| f.path == BENCH).ok_or(format!("{BENCH}: missing"))?;
    rows.extend(bench_rows(bench, &pins)?);

    // Every source the release read, so that one release hash names one overlay and one ledger.
    let mut sources: Vec<Source> = model_files;
    for name in ["Cargo.lock", "Cargo.toml", TEMPLATE, "pins.tsv"] {
        let path = format!("{TOOL}/{name}");
        sources.push(Source { bytes: fs::read(repo.join(&path)).map_err(|e| format!("{path}: {e}"))?, path });
    }
    sources.extend(overlay::regular_files(repo, &format!("{TOOL}/src"))?);
    let json_rs = "tools/tviburi-check/src/json.rs";
    sources.push(Source { path: json_rs.into(), bytes: fs::read(repo.join(json_rs)).map_err(|e| format!("{json_rs}: {e}"))? });

    let mut m = format!("{MANIFEST_FORMAT}\n");
    m += "sources every file the release reads is a source line, its generator included, so that one release hash names one overlay and one ledger (northstar invariant 3)\n";
    for (asm, b) in &pins.builds {
        m += &format!("build {asm} {b}\n");
    }
    for p in &pins.stock {
        m += &format!("stock {} {}\n", p.sha256, p.path);
    }
    for (stock_ty, guard) in &pins.classes {
        m += &format!("class {stock_ty} {guard}\n");
        for (ty, inst, file) in &chain.declared {
            if ty == stock_ty {
                m += &format!("restate {stock_ty} {inst} {file}\n");
            }
        }
    }
    for src in &sources {
        m += &format!("source {} {}\n", sha256::hex(&src.bytes), src.path);
    }
    m += &format!("file {} {TEMPLATE}\n", sha256::hex(&template.bytes));
    m += &format!("overlay {} {} {OVERLAY}\n", sha256::hex(body.as_bytes()), body.len());
    m += &format!("excluded {OVERLAY} the overlay's {} bytes above, then {TEMPLATE} with its placeholder replaced by this manifest's sha256\n", body.len());
    // The ledger's rows never name the release, so the manifest can bind them without naming itself.
    let rows_text = ledger::rows(rows)?;
    m += &format!("ledger-rows {} {LEDGER}\n", sha256::hex(rows_text.as_bytes()));
    m += &format!("excluded {LEDGER} its first line names this manifest's sha256; the lines after it are the ledger-rows above, and its own sha256 is reported beside the manifest's\n");
    let hash = sha256::hex(m.as_bytes());
    let shipped = body + &overlay::fill(overlay::text(&template)?, &hash);
    let ledger = ledger::schema_line(&hash)? + &rows_text;

    fs::create_dir(out).map_err(|e| format!("{}: {e} (the output directory must not exist)", out.display()))?;
    let files: [(&str, &[u8]); 4] =
        [(MANIFEST, m.as_bytes()), (OVERLAY, shipped.as_bytes()), (TEMPLATE, &template.bytes), (LEDGER, ledger.as_bytes())];
    for (name, bytes) in files {
        fs::write(out.join(name), bytes).map_err(|e| format!("{name}: {e}"))?;
    }
    fs::write(out.join(TARBALL), tar::archive(&files)).map_err(|e| format!("{TARBALL}: {e}"))?;
    Ok((hash, sha256::hex(ledger.as_bytes())))
}

/// The consumer's check, line by line: (ok, line) pairs.
fn verify(dir: &Path, pin: &str) -> Vec<(bool, String)> {
    let mut out = Vec::new();
    let mut say = |ok: bool, line: String| out.push((ok, line));
    let read = |name: &str| fs::read(dir.join(name));
    let Ok(manifest) = read(MANIFEST) else {
        say(false, format!("no {MANIFEST}"));
        return out;
    };
    let got = sha256::hex(&manifest);
    if got != pin {
        say(false, format!("{MANIFEST} sha256 {got} is not the pinned {pin} (a stale or another release)"));
        return out;
    }
    say(true, format!("{MANIFEST} is the pinned release sha256:{pin}"));
    let Ok(manifest) = String::from_utf8(manifest) else {
        say(false, format!("{MANIFEST} is not UTF-8"));
        return out;
    };
    let (mut template, mut body, mut rows) = (None, None, None);
    for line in manifest.lines() {
        let f: Vec<&str> = line.split(' ').collect();
        match f.as_slice() {
            ["file", h, name] => match read(name) {
                Ok(b) if sha256::hex(&b) == *h => {
                    say(true, format!("{name} matches the manifest"));
                    if *name == TEMPLATE {
                        template = Some(b);
                    }
                }
                Ok(_) => say(false, format!("{name} does not match the manifest")),
                Err(e) => say(false, format!("{name}: {e}")),
            },
            ["overlay", h, len, name] if *name == OVERLAY => body = len.parse::<usize>().ok().map(|l| (h.to_string(), l)),
            ["ledger-rows", h, name] if *name == LEDGER => rows = Some(h.to_string()),
            _ => {}
        }
    }
    match (read(OVERLAY), template.and_then(|t| String::from_utf8(t).ok()), body) {
        (Err(e), _, _) => say(false, format!("{OVERLAY}: {e}")),
        (_, None, _) | (_, _, None) => say(false, format!("the manifest lists no readable {TEMPLATE} or no {OVERLAY} body")),
        (Ok(shipped), Some(t), Some((h, len))) => {
            let filled = overlay::fill(&t, pin);
            if shipped.len() == len + filled.len() && sha256::hex(&shipped[..len]) == h && shipped[len..] == *filled.as_bytes() {
                say(true, format!("{OVERLAY} is the manifest's body, then {TEMPLATE} filled with the pinned hash"));
            } else {
                say(false, format!("{OVERLAY} is not the manifest's body followed by {TEMPLATE} filled with the pinned hash"));
            }
        }
    }
    match read(LEDGER).map(String::from_utf8) {
        Ok(Ok(t)) => match (ledger::check(&t, pin), rows) {
            (Err(e), _) => say(false, format!("{LEDGER}: {e}")),
            (Ok(_), None) => say(false, format!("the manifest names no ledger-rows for {LEDGER}")),
            (Ok(n), Some(h)) if t.split_once('\n').is_some_and(|(_, r)| sha256::hex(r.as_bytes()) == h) => {
                say(true, format!("{LEDGER} reads strictly for this release and its {n} rows are the manifest's; sha256 {}", sha256::hex(t.as_bytes())))
            }
            (Ok(_), Some(_)) => say(false, format!("{LEDGER}'s rows are not the manifest's ledger-rows")),
        },
        _ => say(false, format!("{LEDGER}: missing or not UTF-8")),
    }
    if let Ok(tarball) = read(TARBALL) {
        let names = [MANIFEST, OVERLAY, TEMPLATE, LEDGER];
        let bytes: Vec<Vec<u8>> = names.iter().map(|n| read(n).unwrap_or_default()).collect();
        let files: Vec<(&str, &[u8])> = names.iter().zip(&bytes).map(|(n, b)| (*n, b.as_slice())).collect();
        if tar::archive(&files) == tarball {
            say(true, format!("{TARBALL} holds exactly these files"));
        } else {
            say(false, format!("{TARBALL} does not hold exactly these files"));
        }
    }
    out
}

/// Gate clause 6's cases for one guard, generated from its field rows in the ledger.
fn cases(ledger_text: &str, guard: &str, base: u32, size: u32) -> Result<String, String> {
    let mut regs: BTreeMap<u32, Vec<(String, u32, u32, Option<Vec<u32>>)>> = BTreeMap::new();
    for line in ledger_text.lines().skip(1) {
        let Value::Object(r) = json::parse(line)? else { continue };
        let get = |k: &str| match r.get(k) {
            Some(Value::Str(v)) => Some(v.clone()),
            _ => None,
        };
        if get("row").as_deref() != Some("field") || get("peripheral").as_deref() != Some(guard) {
            continue;
        }
        let name = format!("{}.{}", get("register").unwrap_or_default(), get("field").unwrap_or_default());
        let offset = get("offset").and_then(|o| u32::from_str_radix(o.trim_start_matches("0x"), 16).ok()).ok_or(format!("{name}: no offset"))?;
        let bits = get("bits").ok_or(format!("{name}: no bits"))?;
        let (msb, lsb) = match bits.split_once(':') {
            Some((a, b)) => (a.parse().map_err(|_| format!("{name}: bits"))?, b.parse().map_err(|_| format!("{name}: bits"))?),
            None => {
                let b: u32 = bits.parse().map_err(|_| format!("{name}: bits"))?;
                (b, b)
            }
        };
        let allowed = match r.get("allowed") {
            Some(Value::Str(a)) if a == "any" => None,
            Some(Value::Array(v)) => Some(v.iter().map(|x| match x {
                Value::Number(d) => d.parse().map_err(|_| format!("{name}: allowed")),
                _ => Err(format!("{name}: allowed")),
            }).collect::<Result<Vec<u32>, String>>()?),
            _ => return Err(format!("{name}: no allowed")),
        };
        regs.entry(offset).or_default().push((name, msb, lsb, allowed));
    }
    if regs.is_empty() {
        return Err(format!("no field rows for {guard}"));
    }
    let mut out = String::new();
    let mut case = |kind: &str, name: &str, offset: u32, value: u32| {
        out += &format!("# CASE {kind} {name} 0x{offset:X} 0x{value:08X}\nsysbus WriteDoubleWord 0x{:08X} 0x{value:08X}\nmachine Reset\n", base + offset);
    };
    for (offset, fields) in &regs {
        let put = |v: u32, msb: u32, lsb: u32, x: u32| {
            let mask = (((1u64 << (msb + 1)) - 1) & !((1u64 << lsb) - 1)) as u32;
            (v & !mask) | ((x << lsb) & mask)
        };
        let baseline = fields.iter().fold(0u32, |v, (_, msb, lsb, a)| put(v, *msb, *lsb, a.as_ref().map_or(0, |a| a[0])));
        let register = fields[0].0.split('.').next().unwrap_or_default();
        case("pass", &format!("{register}.baseline"), *offset, baseline);
        for (name, msb, lsb, allowed) in fields {
            let ones = ((1u64 << (msb - lsb + 1)) - 1) as u32;
            let tried: Vec<u32> = allowed.clone().unwrap_or(vec![ones]);
            for &x in &tried {
                let v = put(baseline, *msb, *lsb, x);
                if v != baseline {
                    case("pass", name, *offset, v);
                }
            }
            if let Some(a) = allowed {
                let bad = std::iter::once(ones).chain(0..=ones).find(|x| !a.contains(x)).ok_or(format!("{name}: no value is disallowed"))?;
                case("refuse", name, *offset, put(baseline, *msb, *lsb, bad));
            }
        }
    }
    for offset in (0..size).step_by(4).filter(|o| !regs.contains_key(o)) {
        case("refuse", "none", offset, 0);
    }
    Ok(out)
}

fn hex_pin(a: &str) -> Option<String> {
    let h = a.strip_prefix("sha256:").unwrap_or(a);
    (h.len() == 64 && h.bytes().all(|b| b.is_ascii_hexdigit() && !b.is_ascii_uppercase())).then(|| h.to_string())
}

fn main() -> ExitCode {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let usage = || {
        eprintln!("usage: tviburi-release build <repo> <renode-root> <out-dir> | verify <dir> <sha256:hex> | cases <ledger> <guard> <base> <size>");
        ExitCode::from(2)
    };
    match args.iter().map(String::as_str).collect::<Vec<_>>().as_slice() {
        ["build", repo, renode, out] => match build(Path::new(repo), Path::new(renode), Path::new(out)) {
            Ok((release, ledger)) => {
                println!("release sha256:{release}\nledger sha256:{ledger}");
                ExitCode::SUCCESS
            }
            Err(e) => {
                eprintln!("tviburi-release: REFUSED: {e}");
                ExitCode::FAILURE
            }
        },
        ["verify", dir, pin] => {
            let Some(pin) = hex_pin(pin) else { return usage() };
            let lines = verify(Path::new(dir), &pin);
            for (ok, line) in &lines {
                println!("{} {line}", if *ok { "ok  " } else { "FAIL" });
            }
            if lines.iter().all(|l| l.0) {
                println!("VERIFY PASS");
                ExitCode::SUCCESS
            } else {
                println!("VERIFY FAIL");
                ExitCode::FAILURE
            }
        }
        ["cases", ledger_path, guard, base, size] => {
            let hex = |x: &str| x.strip_prefix("0x").and_then(|b| u32::from_str_radix(b, 16).ok());
            match (fs::read_to_string(ledger_path).map_err(|e| e.to_string()), hex(base), hex(size)) {
                (Ok(t), Some(b), Some(z)) => match cases(&t, guard, b, z) {
                    Ok(c) => {
                        print!("{c}");
                        ExitCode::SUCCESS
                    }
                    Err(e) => {
                        eprintln!("tviburi-release: {e}");
                        ExitCode::FAILURE
                    }
                },
                _ => usage(),
            }
        }
        _ => usage(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn row(reg: &str, off: u32, name: &str, msb: u32, lsb: u32, allowed: Option<Vec<u32>>, class: &str) -> fields::Row {
        fields::Row { register: reg.into(), offset: off, section: "§1".into(), name: name.into(), msb, lsb, allowed, class: class.into(), behaves_as: "x (A3)".into(), page: 1 }
    }

    #[test]
    fn every_bit_of_every_register_is_in_exactly_one_row() {
        let ok = [row("R", 0, "HI", 31, 1, Some(vec![0]), "reserved"), row("R", 0, "LO", 0, 0, None, "modeled by the guard")];
        assert_eq!(field_rows("G", "f", &ok).unwrap().len(), 2);
        let gap = [row("R", 0, "HI", 31, 2, Some(vec![0]), "reserved"), ok[1].clone()];
        assert!(field_rows("G", "f", &gap).unwrap_err().contains("in no row"));
        let overlap = [row("R", 0, "HI", 31, 0, Some(vec![0]), "reserved"), ok[1].clone()];
        assert!(field_rows("G", "f", &overlap).unwrap_err().contains("another row"));
        let moved = [ok[0].clone(), row("R", 4, "LO", 0, 0, None, "tag")];
        assert!(field_rows("G", "f", &moved).unwrap_err().contains("two offsets"));
        let twin = [ok[0].clone(), ok[1].clone(), row("S", 0, "ALL", 31, 0, Some(vec![0]), "reserved")];
        assert!(field_rows("G", "f", &twin).unwrap_err().contains("at one offset"));
    }

    #[test]
    fn every_class_maps_to_a_rul_16_state_or_stops() {
        for (class, want) in [
            ("modeled by the guard", "guard"),
            ("inherited, unaudited (acknowledged by name)", "inherited-unaudited"),
            ("acknowledged by name", "acknowledged"),
            ("tag", "tag"),
            ("reserved", "reserved"),
            ("read-only", "read-only"),
            ("stock, wrong (byte order, L352)", "stock-wrong"),
            ("undefined in the stock model", "stock-undefined"),
        ] {
            assert_eq!(state(class).unwrap().0, want);
        }
        assert_eq!(state("stock, wrong (byte order, L352)").unwrap().1.as_deref(), Some("byte order, L352"));
        let r = [row("R", 0, "ALL", 31, 0, None, "modelled")];
        assert!(field_rows("G", "f", &r).unwrap_err().contains("a stop"));
        assert_eq!(acknowledgement("no pins (A3)").as_deref(), Some("TVI-WO-0100b.rm0433.md §4 A3"));
        assert_eq!(acknowledgement("x (A2; DS12117)").as_deref(), Some("TVI-WO-0100b.rm0433.md §4 A2"));
        assert_eq!(acknowledgement("x (Ax)"), None);
        let bare = fields::Row { behaves_as: "no reason".into(), ..row("R", 0, "ALL", 31, 0, None, "acknowledged by name") };
        assert!(field_rows("G", "f", &[bare]).unwrap_err().contains("no (A<n>) reason"));
    }

    #[test]
    fn cases_come_from_the_ledger_rows() {
        let rows = field_rows("G", "f", &[
            row("R", 4, "HI", 31, 8, Some(vec![0]), "reserved"),
            row("R", 4, "SZ", 7, 4, Some(vec![7, 0]), "acknowledged by name"),
            row("R", 4, "EN", 3, 0, None, "modeled by the guard"),
        ]).unwrap();
        let led = ledger::schema_line(&"0".repeat(64)).unwrap() + &ledger::rows(rows).unwrap();
        let c = cases(&led, "G", 0x4000_0000, 0xC).unwrap();
        let heads: Vec<&str> = c.lines().filter(|l| l.starts_with("# CASE")).collect();
        assert_eq!(heads, [
            "# CASE pass R.baseline 0x4 0x00000070",
            "# CASE pass R.EN 0x4 0x0000007F",
            "# CASE refuse R.HI 0x4 0xFFFFFF70",
            "# CASE pass R.SZ 0x4 0x00000000",
            "# CASE refuse R.SZ 0x4 0x000000F0",
            "# CASE refuse none 0x0 0x00000000",
            "# CASE refuse none 0x8 0x00000000",
        ]);
        assert!(c.contains("sysbus WriteDoubleWord 0x40000004 0x0000007F\nmachine Reset\n"));
    }

    #[test]
    fn the_tarball_is_fixed_whatever_the_files_came_from() {
        let a = tar::archive(&[("A", b"x"), ("B", b"")]);
        assert_eq!(a, tar::archive(&[("A", b"x"), ("B", b"")]));
        assert_eq!(a.len(), 512 * 2 + 512 + 1024);
        assert_eq!(&a[257..263], b"ustar\0");
    }
}
