//! The pinned stock platform files (the `stm32h753.repl` include chain), read for the class map's
//! cancel-and-restate instances (RUL-09 (b)) and the ledger's inherited, tag and canned rows (RUL-16,
//! northstar invariant 2). Each file is checked against its pinned sha256 before it is read, and every
//! top-level line must be one this reader understands, or the release refuses.

use std::fs;
use std::path::Path;

/// One pinned file: its path under Renode's root, and its sha256.
pub struct Pin {
    pub path: String,
    pub sha256: String,
}

/// What the chain declares.
#[derive(Default)]
pub struct Chain {
    /// (type string, instance, file): every typed declaration that is not a Python stub.
    pub declared: Vec<(String, String, String)>,
    /// (instance, file, what it answers): a `Python.PythonPeripheral`, or a declaration with an `init` block.
    pub canned: Vec<(String, String, String, String)>,
    /// (name, range, file): each `Tag` in `sysbus`'s `init` block.
    pub tags: Vec<(String, String, String)>,
}

/// Read the chain from its first pin, following `using` lines; every pin must be reached, in order, and
/// nothing else.
pub fn read(renode: &Path, pins: &[Pin]) -> Result<Chain, String> {
    let mut chain = Chain::default();
    let mut next = Some(pins.first().ok_or("no stock pin")?.path.clone());
    let mut i = 0;
    while let Some(path) = next.take() {
        let pin = pins.get(i).ok_or(format!("{path}: included, but not pinned"))?;
        if pin.path != path {
            return Err(format!("{path}: included where the pins name {} next", pin.path));
        }
        let bytes = fs::read(renode.join(&path)).map_err(|e| format!("{path}: {e}"))?;
        let got = crate::sha256::hex(&bytes);
        if got != pin.sha256 {
            return Err(format!("{path}: sha256 {got}, not the pinned {}", pin.sha256));
        }
        let text = std::str::from_utf8(&bytes).map_err(|_| format!("{path}: not UTF-8"))?;
        next = file(&path, text, &mut chain)?;
        i += 1;
    }
    if i != pins.len() {
        return Err(format!("{}: pinned, but never included", pins[i].path));
    }
    Ok(chain)
}

/// Read one file; return the file its `using` line includes, if any.
fn file(path: &str, text: &str, chain: &mut Chain) -> Result<Option<String>, String> {
    let mut include = None;
    let mut current: Option<(String, Option<String>)> = None; // (instance, its type when declared here)
    let mut in_script = false;
    if text.contains('\r') {
        return Err(format!("{path}: a CR"));
    }
    let mut init_indent: Option<usize> = None; // the indentation of the current `init:` line
    for (n, line) in text.lines().enumerate() {
        let at = format!("{path}:{}", n + 1);
        if in_script {
            in_script = line.matches("'''").count() % 2 == 0;
            continue;
        }
        if line.matches("'''").count() % 2 == 1 {
            in_script = true; // a Python script block, whose lines start at column 0
            continue;
        }
        let t = line.trim();
        if t.is_empty() || t.starts_with("//") {
            continue;
        }
        let indent = line.len() - line.trim_start().len();
        if indent > 0 {
            let (name, ty) = current.as_ref().ok_or(format!("{at}: an indented line before any entry"))?;
            if init_indent.is_some_and(|i| indent <= i) {
                init_indent = None;
            }
            if t == "init:" {
                init_indent = Some(indent);
            } else if init_indent.is_some() && name == "sysbus" {
                chain.tags.push(tag(t, path, &at)?);
            } else if init_indent.is_some() && !chain.canned.iter().any(|c| c.0 == *name && c.1 == path) {
                // An init command on a declared instance writes fixed values: the instance answers canned.
                let ty = ty.clone().ok_or(format!("{at}: an init block on an instance this file does not declare"))?;
                chain.canned.push((name.clone(), path.into(), ty, "values written by its stock init block".into()));
            }
            continue;
        }
        init_indent = None;
        if let Some(rest) = t.strip_prefix("using ") {
            if include.is_some() {
                return Err(format!("{at}: a second `using`"));
            }
            let target = rest.trim().strip_prefix('"').and_then(|r| r.strip_suffix('"')).ok_or(format!("{at}: a `using` this reader cannot read"))?;
            let dir = Path::new(path).parent().map(|p| p.to_string_lossy().into_owned()).unwrap_or_default();
            include = Some(match target.strip_prefix("./") {
                Some(rel) => format!("{dir}/{rel}"),
                None => target.to_string(),
            });
            continue;
        }
        let (name, rest) = t.split_once(':').ok_or(format!("{at}: a top-level line this reader cannot read: {t}"))?;
        if name.is_empty() || !name.chars().all(|c| c.is_ascii_alphanumeric() || c == '_') {
            return Err(format!("{at}: a top-level line this reader cannot read: {t}"));
        }
        let rest = rest.trim();
        if rest.is_empty() {
            current = Some((name.into(), None)); // an update of an instance declared elsewhere: no row
            continue;
        }
        let (ty, _) = rest.split_once(" @ ").ok_or(format!("{at}: a declaration without ` @ `: {t}"))?;
        if ty.is_empty() || !ty.split('.').all(|p| !p.is_empty() && p.chars().all(|c| c.is_ascii_alphanumeric() || c == '_')) {
            return Err(format!("{at}: a type string this reader cannot read: {ty}"));
        }
        if ty == "Python.PythonPeripheral" {
            chain.canned.push((name.into(), path.into(), ty.into(), "a stock Python script's fixed answers".into()));
        } else {
            chain.declared.push((ty.into(), name.into(), path.into()));
        }
        current = Some((name.into(), Some(ty.into())));
    }
    if in_script {
        return Err(format!("{path}: a Python script block never closes"));
    }
    Ok(include)
}

/// `Tag <0xA, 0xB> "NAME"`: (NAME, "0xA-0xB", file).
fn tag(t: &str, path: &str, at: &str) -> Result<(String, String, String), String> {
    let bad = || format!("{at}: a line in sysbus's init block that is not a `Tag <a, b> \"NAME\"`: {t}");
    let rest = t.strip_prefix("Tag <").ok_or_else(bad)?;
    let (range, name) = rest.split_once("> ").ok_or_else(bad)?;
    let (a, b) = range.split_once(", ").ok_or_else(bad)?;
    let name = name.strip_prefix('"').and_then(|n| n.strip_suffix('"')).ok_or_else(bad)?;
    let hex = |x: &str| x.strip_prefix("0x").is_some_and(|h| !h.is_empty() && h.chars().all(|c| c.is_ascii_hexdigit()));
    if !hex(a) || !hex(b) || name.is_empty() || name.contains('"') {
        return Err(bad());
    }
    Ok((name.into(), format!("{a}-{b}"), path.into()))
}

#[cfg(test)]
mod tests {
    use super::*;

    const H7: &str = "cpu: CPU.CortexM @ sysbus\n    cpuType: \"cortex-m7\"\n\nspi4: SPI.STM32H7_SPI @ {\n        sysbus 0x40013400\n    }\n\
// a comment\ntimer1:\n    0 -> gpioPortA#07@01\n\nmem: Memory.MappedMemory @ sysbus 0x1FF00000\n    size: 0x20000\n    init:\n        WriteWord 0x1E880 0x800\n\n\
pwr: Python.PythonPeripheral @ sysbus 0x58024800\n    size: 0x400\n    script: '''\nif request.Offset == 0x00: # CR1\n    request.Value = 1\n'''\n\n\
sysbus:\n    init:\n        Tag <0x40013000, 0x400133FF> \"SPI1_/_I2S1\"\n";

    #[test]
    fn reads_declarations_stubs_inits_and_tags() {
        let mut c = Chain::default();
        assert_eq!(file("platforms/cpus/h7.repl", H7, &mut c).unwrap(), None);
        let names: Vec<_> = c.declared.iter().map(|d| (d.0.as_str(), d.1.as_str())).collect();
        assert_eq!(names, [("CPU.CortexM", "cpu"), ("SPI.STM32H7_SPI", "spi4"), ("Memory.MappedMemory", "mem")]);
        let canned: Vec<_> = c.canned.iter().map(|d| d.0.as_str()).collect();
        assert_eq!(canned, ["mem", "pwr"]);
        assert_eq!(c.tags, [("SPI1_/_I2S1".into(), "0x40013000-0x400133FF".into(), "platforms/cpus/h7.repl".into())]);
    }

    #[test]
    fn follows_using_and_refuses_what_it_cannot_read() {
        let mut c = Chain::default();
        assert_eq!(file("platforms/cpus/a.repl", "using \"./b.repl\"\n", &mut c).unwrap().as_deref(), Some("platforms/cpus/b.repl"));
        assert_eq!(file("p/a.repl", "using \"platforms/x.repl\"\n", &mut c).unwrap().as_deref(), Some("platforms/x.repl"));
        for (bad, why) in [
            ("x = 1\n", "cannot read"),
            ("a: B.C\n", "without ` @ `"),
            ("a: B-C @ sysbus\n", "type string"),
            ("sysbus:\n    init:\n        Tag 0x1 \"X\"\n", "not a `Tag"),
            ("p: Python.PythonPeripheral @ sysbus 0\n    script: '''\nx\n", "never closes"),
            ("a: B @ sysbus\r\n", "a CR"),
        ] {
            let e = file("f", bad, &mut Chain::default()).err().unwrap_or_default();
            assert!(e.contains(why), "{bad:?}: {e}");
        }
    }
}
