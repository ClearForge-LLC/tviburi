//! A strict reader of a guard's allow-list (RUL-16, RUL-17): the `Fields` table of `R(...)` rows the guard
//! runs on, read from its C# source, never typed again. Every line of the table must be a row this reader
//! can read whole; each symbol a row names (`Zero`, `Guard`, `CrcPolyReset`, ...) is resolved from the same
//! file's own declaration of it. Anything else refuses by name.

use std::collections::BTreeMap;

const TABLE_START: &str = "private static readonly Row[] Fields =";

/// One row of the table, as the guard reads it.
#[derive(Debug, Clone, PartialEq)]
pub struct Row {
    pub register: String,
    pub offset: u32,
    pub section: String,
    pub name: String,
    pub msb: u32,
    pub lsb: u32,
    /// None: any value.
    pub allowed: Option<Vec<u32>>,
    pub class: String,
    pub behaves_as: String,
    pub page: u32,
}

/// Does this file hold a `Fields` table?
pub fn has_table(text: &str) -> bool {
    text.contains(TABLE_START)
}

/// Read every row of the file's `Fields` table.
pub fn read(path: &str, text: &str) -> Result<Vec<Row>, String> {
    let lines: Vec<&str> = text.lines().collect();
    let start = lines.iter().position(|l| l.trim() == TABLE_START).ok_or(format!("{path}: no `{TABLE_START}`"))?;
    if lines.get(start + 1).map(|l| l.trim()) != Some("{") {
        return Err(format!("{path}:{}: `{{` expected after the table's declaration", start + 2));
    }
    let symbols = Symbols::read(text);
    let mut rows = Vec::new();
    for (i, line) in lines.iter().enumerate().skip(start + 2) {
        let at = format!("{path}:{}", i + 1);
        let t = line.trim();
        if t == "};" {
            if rows.is_empty() {
                return Err(format!("{at}: an empty table"));
            }
            return Ok(rows);
        }
        let inner = t
            .strip_prefix("R(")
            .and_then(|r| r.strip_suffix("),"))
            .ok_or(format!("{at}: a line in the Fields table that is not one `R(...),` row: {t}"))?;
        rows.push(row(&split(inner, &at)?, &symbols, &at)?);
    }
    Err(format!("{path}: the Fields table never closes"))
}

/// The arguments of one row, split at top-level commas (outside strings and braces).
fn split(s: &str, at: &str) -> Result<Vec<String>, String> {
    let (mut out, mut cur, mut depth, mut in_str, mut esc) = (Vec::new(), String::new(), 0, false, false);
    for c in s.chars() {
        if in_str {
            cur.push(c);
            if esc {
                esc = false;
            } else if c == '\\' {
                esc = true;
            } else if c == '"' {
                in_str = false;
            }
            continue;
        }
        match c {
            '"' => {
                in_str = true;
                cur.push(c);
            }
            '{' => {
                depth += 1;
                cur.push(c);
            }
            '}' => {
                depth -= 1;
                cur.push(c);
            }
            ',' if depth == 0 => out.push(std::mem::take(&mut cur).trim().to_string()),
            _ => cur.push(c),
        }
        if depth < 0 {
            return Err(format!("{at}: unbalanced braces"));
        }
    }
    if in_str || depth != 0 {
        return Err(format!("{at}: an unterminated string or brace"));
    }
    out.push(cur.trim().to_string());
    Ok(out)
}

fn row(a: &[String], sym: &Symbols, at: &str) -> Result<Row, String> {
    if a.len() != 10 {
        return Err(format!("{at}: {} arguments, not the row's 10", a.len()));
    }
    let row = Row {
        register: string(&a[0], sym, at)?,
        offset: number(&a[1], sym, at)?,
        section: string(&a[2], sym, at)?,
        name: string(&a[3], sym, at)?,
        msb: number(&a[4], sym, at)?,
        lsb: number(&a[5], sym, at)?,
        allowed: values(&a[6], sym, at)?,
        class: string(&a[7], sym, at)?,
        behaves_as: string(&a[8], sym, at)?,
        page: number(&a[9], sym, at)?,
    };
    if row.msb > 31 || row.lsb > row.msb {
        return Err(format!("{at}: bits {}:{} are not within 31:0", row.msb, row.lsb));
    }
    let max = if row.msb - row.lsb == 31 { u32::MAX } else { (1u32 << (row.msb - row.lsb + 1)) - 1 };
    if let Some(v) = row.allowed.as_ref().and_then(|vs| vs.iter().find(|&&v| v > max)) {
        return Err(format!("{at}: allowed value {v:#x} does not fit bits {}:{}", row.msb, row.lsb));
    }
    Ok(row)
}

/// A C# string literal (only the escapes \" and \\), or a `const string` symbol.
fn string(a: &str, sym: &Symbols, at: &str) -> Result<String, String> {
    if let Some(body) = a.strip_prefix('"').and_then(|r| r.strip_suffix('"')) {
        return unescape(body).ok_or(format!("{at}: a string literal this reader cannot read: {a}"));
    }
    sym.strings.get(a).cloned().ok_or(format!("{at}: `{a}` is neither a string literal nor a `const string` of this file"))
}

fn unescape(body: &str) -> Option<String> {
    let mut out = String::new();
    let mut chars = body.chars();
    while let Some(c) = chars.next() {
        match c {
            '\\' => match chars.next()? {
                '"' => out.push('"'),
                '\\' => out.push('\\'),
                _ => return None,
            },
            '"' => return None,
            c => out.push(c),
        }
    }
    Some(out)
}

/// A decimal or 0x-hex literal, or a `const uint`/`const long` symbol.
fn number(a: &str, sym: &Symbols, at: &str) -> Result<u32, String> {
    literal(a).or_else(|| sym.numbers.get(a).copied()).ok_or(format!("{at}: `{a}` is neither a number nor a numeric `const` of this file"))
}

fn literal(a: &str) -> Option<u32> {
    let a = a.strip_suffix('u').unwrap_or(a);
    match a.strip_prefix("0x") {
        Some(h) if !h.is_empty() && h.chars().all(|c| c.is_ascii_hexdigit()) => u32::from_str_radix(h, 16).ok(),
        None if !a.is_empty() && a.chars().all(|c| c.is_ascii_digit()) => a.parse().ok(),
        _ => None,
    }
}

/// `new uint[] { a, b }`, or a `static readonly uint[]` symbol (`null`: any value).
fn values(a: &str, sym: &Symbols, at: &str) -> Result<Option<Vec<u32>>, String> {
    if let Some(list) = a.strip_prefix("new uint[] {").and_then(|r| r.strip_suffix('}')) {
        return list.split(',').map(|v| number(v.trim(), sym, at)).collect::<Result<Vec<_>, _>>().map(Some);
    }
    sym.arrays.get(a).cloned().ok_or(format!("{at}: `{a}` is neither `new uint[] {{ ... }}` nor a `uint[]` of this file"))
}

/// The file's own `const string`, numeric `const` and `static readonly uint[]` declarations, by name.
struct Symbols {
    strings: BTreeMap<String, String>,
    numbers: BTreeMap<String, u32>,
    arrays: BTreeMap<String, Option<Vec<u32>>>,
}

impl Symbols {
    fn read(text: &str) -> Symbols {
        let mut s = Symbols { strings: BTreeMap::new(), numbers: BTreeMap::new(), arrays: BTreeMap::new() };
        // Numbers first: an array may name one.
        for line in text.lines() {
            if let Some((name, v)) = decl(line, &["private const uint ", "private const long "]) {
                if let Some(n) = literal(v) {
                    s.numbers.insert(name.into(), n);
                }
            }
        }
        for line in text.lines() {
            if let Some((name, v)) = decl(line, &["private const string "]) {
                if let Some(body) = v.strip_prefix('"').and_then(|r| r.strip_suffix('"')).and_then(unescape) {
                    s.strings.insert(name.into(), body);
                }
            } else if let Some((name, v)) = decl(line, &["private static readonly uint[] "]) {
                let parsed = if v == "null" {
                    Some(None)
                } else {
                    v.strip_prefix('{').and_then(|r| r.strip_suffix('}')).and_then(|list| {
                        list.split(',').map(|x| literal(x.trim()).or_else(|| s.numbers.get(x.trim()).copied())).collect::<Option<Vec<_>>>().map(Some)
                    })
                };
                if let Some(p) = parsed {
                    s.arrays.insert(name.into(), p);
                }
            }
        }
        s
    }
}

/// `<prefix><Name> = <value>;` on one line: (Name, value).
fn decl<'a>(line: &'a str, prefixes: &[&str]) -> Option<(&'a str, &'a str)> {
    let t = line.trim();
    let rest = prefixes.iter().find_map(|p| t.strip_prefix(p))?;
    let (name, v) = rest.split_once(" = ")?;
    // The value ends at the first `;` outside a string; only a comment may follow it.
    let (mut in_str, mut esc) = (false, false);
    let end = v.char_indices().find_map(|(i, c)| {
        match (in_str, esc, c) {
            (true, true, _) => esc = false,
            (true, false, '\\') => esc = true,
            (true, false, '"') | (false, _, '"') => in_str = !in_str,
            (false, _, ';') => return Some(i),
            _ => {}
        }
        None
    })?;
    let tail = v[end + 1..].trim();
    (tail.is_empty() || tail.starts_with("//")).then(|| (name.trim(), v[..end].trim()))
}

#[cfg(test)]
mod tests {
    use super::*;

    const FILE: &str = r#"
        private static readonly uint[] Any = null;
        private static readonly uint[] Zero = { 0 };
        private const string Guard = "modeled by the guard";
        private const uint CrcPolyReset = 0x107; // p2228
        private static readonly Row[] Fields =
        {
            R("SPI_CR1", 0x00, "§50.11.1", "Res31_17", 31, 17, Zero, Guard, "0", 2215),
            R("SPI_CRCPOLY", 0x40, "§50.11.10", "CRCPOLY", 31, 0, new uint[] { CrcPolyReset }, "read-only", "a \"q\"", 2228),
            R("SPI_X", 0x08, "§1", "F", 4, 0, new uint[] { 7, 0 }, Guard, "x", 1),
            R("SPI_Y", 0x0C, "§1", "G", 0, 0, Any, Guard, "y", 1),
        };
    "#;

    #[test]
    fn reads_every_row_form_and_resolves_its_symbols() {
        let rows = read("f", FILE).unwrap();
        assert_eq!(rows.len(), 4);
        assert_eq!(rows[0].allowed, Some(vec![0]));
        assert_eq!(rows[0].class, "modeled by the guard");
        assert_eq!((rows[1].offset, rows[1].allowed.clone(), rows[1].behaves_as.as_str()), (0x40, Some(vec![0x107]), "a \"q\""));
        assert_eq!(rows[2].allowed, Some(vec![7, 0]));
        assert_eq!(rows[3].allowed, None);
    }

    #[test]
    fn refuses_a_line_or_symbol_it_cannot_read() {
        for (from, to, why) in [
            ("Zero, Guard, \"0\", 2215),", "Zero, Guard, \"0\", 2215)", "not one `R(...),` row"),
            ("Zero, Guard, \"0\", 2215),", "Zero, Guard, \"0\"),", "9 arguments"),
            ("Zero, Guard, \"0\", 2215),", "Zero, Missing, \"0\", 2215),", "`Missing`"),
            ("Zero, Guard, \"0\", 2215),", "Nothing, Guard, \"0\", 2215),", "`Nothing`"),
            ("{ CrcPolyReset }", "{ Unknown }", "`Unknown`"),
            ("31, 17, Zero", "17, 31, Zero", "not within 31:0"),
            ("4, 0, new uint[] { 7, 0 }", "1, 0, new uint[] { 7, 0 }", "does not fit"),
            ("            R(\"SPI_Y\"", "            // a comment\n            R(\"SPI_Y\"", "not one `R(...),` row"),
            ("\"a \\\"q\\\"\"", "\"a \\n\"", "cannot read"),
        ] {
            assert_eq!(FILE.matches(from).count(), 1, "{from}");
            let e = read("f", &FILE.replace(from, to)).err().unwrap_or_default();
            assert!(e.contains(why), "{to}: {e}");
        }
    }
}
