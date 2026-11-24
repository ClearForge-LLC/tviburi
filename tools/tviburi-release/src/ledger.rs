//! The gap ledger (RUL-03, RUL-16, RUL-17): canonical JSON Lines. A schema line naming the release hash,
//! then one row per line, sorted bytewise by (peripheral, register, field) with no key twice. A line is
//! canonical when it is exactly what `write` gives for its value: keys sorted, no whitespace, integers in
//! decimal, strings escaped only where JSON requires. UTF-8, LF, no trailing whitespace.

use crate::json::{self, Value};
use std::collections::BTreeMap;

pub const SCHEMA: u32 = 2;   // 2 (TVI-WO-1305): a partly guarded type's model row is split, `field` "guarded" / "unguarded"
pub const STATES: [&str; 9] =
    ["guard", "inherited-unaudited", "acknowledged", "tag", "reserved", "canned", "read-only", "stock-wrong", "stock-undefined"];
pub const ROWS: [&str; 5] = ["bench", "canned", "field", "model", "tag"];

/// A row under construction: key → value.
pub type Row = BTreeMap<String, Value>;

pub fn s(v: &str) -> Value {
    Value::Str(v.into())
}

pub fn n(v: u32) -> Value {
    Value::Number(v.to_string())
}

/// Canonical text of a value. Only what the ledger uses: objects, arrays, strings, non-negative integers.
pub fn write(v: &Value) -> Result<String, String> {
    Ok(match v {
        Value::Str(t) => {
            let mut o = String::from('"');
            for c in t.chars() {
                match c {
                    '"' => o.push_str("\\\""),
                    '\\' => o.push_str("\\\\"),
                    c if (c as u32) < 0x20 => o.push_str(&format!("\\u{:04x}", c as u32)),
                    c => o.push(c),
                }
            }
            o.push('"');
            o
        }
        Value::Number(d) if d == "0" || (!d.starts_with('0') && !d.is_empty() && d.bytes().all(|b| b.is_ascii_digit())) => d.clone(),
        Value::Array(a) => format!("[{}]", a.iter().map(write).collect::<Result<Vec<_>, _>>()?.join(",")),
        Value::Object(m) => object(m)?,
        other => return Err(format!("not a ledger value: {other:?}")),
    })
}

fn object(m: &Row) -> Result<String, String> {
    let parts = m.iter().map(|(k, v)| Ok(format!("{}:{}", write(&s(k))?, write(v)?))).collect::<Result<Vec<_>, String>>()?;
    Ok(format!("{{{}}}", parts.join(",")))
}

fn key(r: &Row) -> Result<(String, String, String), String> {
    let get = |k: &str| match r.get(k) {
        Some(Value::Str(v)) => Ok(v.clone()),
        _ => Err(format!("a row without a string `{k}`")),
    };
    Ok((get("peripheral")?, get("register")?, get("field")?))
}

/// The rows' lines, sorted; a key twice refuses. They never name the release.
pub fn rows(mut rows: Vec<Row>) -> Result<String, String> {
    let mut keyed = rows.drain(..).map(|r| Ok((key(&r)?, r))).collect::<Result<Vec<_>, String>>()?;
    keyed.sort_by(|a, b| a.0.cmp(&b.0));
    let mut out = String::new();
    for (i, (k, r)) in keyed.iter().enumerate() {
        if i > 0 && keyed[i - 1].0 == *k {
            return Err(format!("a ledger key twice: {k:?}"));
        }
        out.push_str(&object(r)?);
        out.push('\n');
    }
    Ok(out)
}

pub fn schema_line(release_hex: &str) -> Result<String, String> {
    let mut m = Row::new();
    m.insert("ledger".into(), s("tviburi"));
    m.insert("release".into(), s(&format!("sha256:{release_hex}")));
    m.insert("schema".into(), n(SCHEMA));
    Ok(object(&m)? + "\n")
}

/// Read a ledger strictly, for the release `release_hex`; the number of rows, or why it is refused.
pub fn check(text: &str, release_hex: &str) -> Result<usize, String> {
    if text.contains('\r') {
        return Err("a CR".into());
    }
    let body = text.strip_suffix('\n').ok_or("does not end with LF")?;
    let mut lines = body.split('\n');
    let first = lines.next().unwrap_or_default();
    if first != schema_line(release_hex)?.trim_end() {
        return Err(format!("the first line is not the schema line for sha256:{release_hex}: {first}"));
    }
    let mut last: Option<(String, String, String)> = None;
    let mut count = 0;
    for (i, line) in lines.enumerate() {
        let at = format!("line {}", i + 2);
        if line.is_empty() || line != line.trim() {
            return Err(format!("{at}: empty, or leading or trailing whitespace"));
        }
        let v = json::parse(line).map_err(|e| format!("{at}: {e}"))?;
        if write(&v).map_err(|e| format!("{at}: {e}"))? != line {
            return Err(format!("{at}: not canonical"));
        }
        let Value::Object(r) = v else { return Err(format!("{at}: not an object")) };
        let kind = match r.get("row") {
            Some(Value::Str(k)) if ROWS.contains(&k.as_str()) => k.clone(),
            _ => return Err(format!("{at}: no `row` of {ROWS:?}")),
        };
        match (kind.as_str(), r.get("state")) {
            ("bench", None) => {}
            ("bench", Some(_)) => return Err(format!("{at}: a bench row with a state")),
            (_, Some(Value::Str(st))) if STATES.contains(&st.as_str()) => {}
            _ => return Err(format!("{at}: no `state` of {STATES:?}")),
        }
        let k = key(&r).map_err(|e| format!("{at}: {e}"))?;
        if last.as_ref().is_some_and(|l| *l >= k) {
            return Err(format!("{at}: out of order, or a key twice: {k:?}"));
        }
        last = Some(k);
        count += 1;
    }
    Ok(count)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn row(p: &str, r: &str, f: &str) -> Row {
        let mut m = Row::new();
        for (k, v) in [("row", "field"), ("peripheral", p), ("register", r), ("field", f), ("state", "guard"), ("text", "a \"q\" é\t")] {
            m.insert(k.into(), s(v));
        }
        m.insert("allowed".into(), Value::Array(vec![n(7), n(0)]));
        m
    }

    const H: &str = "00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff";

    #[test]
    fn writes_sorted_canonical_lines_that_read_back() {
        let t = schema_line(H).unwrap() + &rows(vec![row("B", "R", "f"), row("A", "S", "a"), row("A", "R", "z")]).unwrap();
        let lines: Vec<&str> = t.lines().collect();
        assert_eq!(lines[0], format!(r#"{{"ledger":"tviburi","release":"sha256:{H}","schema":2}}"#));
        assert_eq!(lines[1], r#"{"allowed":[7,0],"field":"z","peripheral":"A","register":"R","row":"field","state":"guard","text":"a \"q\" é\u0009"}"#);
        assert!(lines[2].contains(r#""register":"S""#) && lines[3].contains(r#""peripheral":"B""#));
        assert_eq!(check(&t, H), Ok(3));
        assert!(rows(vec![row("A", "R", "f"), row("A", "R", "f")]).unwrap_err().contains("twice"));
    }

    #[test]
    fn the_reader_refuses_order_cr_trailing_space_and_no_schema() {
        let t = schema_line(H).unwrap() + &rows(vec![row("A", "R", "a"), row("A", "R", "b")]).unwrap();
        let lines: Vec<&str> = t.lines().collect();
        let swapped = format!("{}\n{}\n{}\n", lines[0], lines[2], lines[1]);
        for (bad, why) in [
            (swapped, "out of order"),
            (t.replacen('\n', "\r\n", 1), "a CR"),
            (t.trim_end().to_string() + " \n", "whitespace"),
            (lines[1..].join("\n") + "\n", "schema line"),
            (t.replace(H, &H.replace('0', "1")), "schema line"),
            (t.replace(r#""allowed":[7,0],"field""#, r#""field":"a","allowed":[7,0],"x""#), "not canonical"),
            (t.replace("[7,0]", "[7,00]"), ""),
            (t.replace(r#""state":"guard""#, r#""state":"modeled""#), "state"),
            (t.trim_end().to_string(), "LF"),
        ] {
            let e = check(&bad, H).err().unwrap_or_default();
            assert!(!e.is_empty() && e.contains(why), "{why}: {e}");
        }
    }
}
