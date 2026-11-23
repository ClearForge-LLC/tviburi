//! The one-file overlay (RUL-09 (a)): every `model/*.cs`, in byte order of name, with their `using`
//! directives hoisted, deduplicated and sorted; then the release constant's template, filled.
//! Anything this cannot read exactly refuses by name, so nothing compiles that the release did not take.

use std::fs;
use std::path::Path;

/// The placeholder the template carries, exactly once, on its constant's line.
pub const PLACEHOLDER: &str = "@@MANIFEST_SHA256@@";
const CONSTANT_LINE: &str = "public static readonly string ManifestSha256 = \"sha256:@@MANIFEST_SHA256@@\";";

/// A file read from the repository: its path relative to the root, and its bytes.
pub struct Source {
    pub path: String,
    pub bytes: Vec<u8>,
}

/// Every entry of `dir`, which must all be regular files, in byte order of name.
pub fn regular_files(root: &Path, rel: &str) -> Result<Vec<Source>, String> {
    let dir = root.join(rel);
    let mut names = Vec::new();
    for entry in fs::read_dir(&dir).map_err(|e| format!("{rel}: {e}"))? {
        let entry = entry.map_err(|e| format!("{rel}: {e}"))?;
        let name = entry.file_name().into_string().map_err(|n| format!("{rel}/{n:?}: a name that is not UTF-8"))?;
        let meta = fs::symlink_metadata(entry.path()).map_err(|e| format!("{rel}/{name}: {e}"))?;
        if !meta.file_type().is_file() {
            return Err(format!(
                "{rel}/{name}: not a regular file (a symbolic link, a directory or another kind); the release reads only regular files, so it would miss what this holds"
            ));
        }
        names.push(name);
    }
    names.sort_by(|a, b| a.as_bytes().cmp(b.as_bytes()));
    names
        .into_iter()
        .map(|name| {
            let path = format!("{rel}/{name}");
            fs::read(dir.join(&name)).map(|bytes| Source { path: path.clone(), bytes }).map_err(|e| format!("{path}: {e}"))
        })
        .collect()
}

/// The model files among `model/`'s entries: names ending in `.cs`. A name ending in `.cs` in another case refuses.
pub fn models(entries: &[Source]) -> Result<Vec<&Source>, String> {
    let mut models = Vec::new();
    for s in entries {
        if s.path.ends_with(".cs") {
            models.push(s);
        } else if s.path.to_ascii_lowercase().ends_with(".cs") {
            return Err(format!("{}: a C# name in another case; name it *.cs, or move it out of model/", s.path));
        }
    }
    if models.is_empty() {
        return Err("model/: no *.cs file".into());
    }
    Ok(models)
}

/// The text of a source file the overlay takes: UTF-8, no byte-order mark, no CR, ending in a newline.
pub fn text(s: &Source) -> Result<&str, String> {
    if s.bytes.starts_with(b"\xEF\xBB\xBF") {
        return Err(format!("{}: a byte-order mark", s.path));
    }
    if s.bytes.contains(&b'\r') {
        return Err(format!("{}: a CR byte", s.path));
    }
    let t = std::str::from_utf8(&s.bytes).map_err(|_| format!("{}: not UTF-8", s.path))?;
    if !t.ends_with('\n') {
        return Err(format!("{}: does not end with a newline", s.path));
    }
    Ok(t)
}

enum Using {
    /// Not a `using` directive (any other line, or a `using` statement or declaration).
    No,
    /// `using A.B.C;`: the namespace.
    Namespace(String),
}

/// Classify one line. A `using` line that is a directive this release cannot hoist without changing its
/// meaning (an alias, `using static`, `global using`), or cannot read, refuses.
fn using(line: &str, at: &str) -> Result<Using, String> {
    let t = line.trim();
    if t.starts_with("global using") {
        return Err(format!("{at}: `global using` is not hoisted"));
    }
    let Some(rest) = t.strip_prefix("using ") else { return Ok(Using::No) };
    let rest = rest.trim_start();
    if rest.starts_with('(') || rest.starts_with("var ") {
        return Ok(Using::No); // a using statement or declaration, inside a method
    }
    if rest.starts_with("static ") {
        return Err(format!("{at}: `using static` changes meaning when hoisted into another file's scope"));
    }
    let name = rest.strip_suffix(';').map(str::trim).unwrap_or("");
    if name.contains('=') {
        return Err(format!("{at}: a `using` alias changes meaning when hoisted into another file's scope"));
    }
    let ident = |p: &str| {
        let mut c = p.chars();
        matches!(c.next(), Some(f) if f.is_ascii_alphabetic() || f == '_') && c.all(|x| x.is_ascii_alphanumeric() || x == '_')
    };
    if name.is_empty() || !name.split('.').all(ident) {
        return Err(format!("{at}: a `using` line this release cannot read: {t}"));
    }
    Ok(Using::Namespace(name.to_string()))
}

/// The overlay's body: everything before the filled constant.
pub fn body(models: &[&Source]) -> Result<String, String> {
    let mut usings: Vec<String> = Vec::new();
    let mut parts = Vec::new();
    for s in models {
        let t = text(s)?;
        let mut kept = String::new();
        let mut header = true;
        for (n, line) in t.split_inclusive('\n').enumerate() {
            let at = format!("{}:{}", s.path, n + 1);
            match using(line, &at)? {
                Using::Namespace(ns) if header => usings.push(format!("using {ns};")),
                Using::Namespace(_) => {
                    return Err(format!("{at}: a `using` directive after a declaration or inside a namespace; the release hoists only those before the first declaration"))
                }
                Using::No => {
                    let l = line.trim();
                    if !(l.is_empty() || l.starts_with("//")) {
                        header = false;
                    }
                    kept.push_str(line);
                }
            }
        }
        parts.push((s.path.as_str(), kept));
    }
    usings.sort_by(|a, b| a.as_bytes().cmp(b.as_bytes()));
    usings.dedup();
    let mut out = String::from(
        "// Tviburi's overlay as one C# file (RUL-09 (a)), written by tools/tviburi-release: every model/*.cs in byte\n\
         // order of name with its `using` directives hoisted here, then the release constant. Do not edit: a consumer\n\
         // checks these bytes against the release's MANIFEST (RUL-10 (f)).\n",
    );
    for u in &usings {
        out.push_str(u);
        out.push('\n');
    }
    for (path, kept) in parts {
        out.push_str(&format!("\n// ---- {path} ----\n"));
        out.push_str(&kept);
    }
    out.push('\n');
    Ok(out)
}

/// Check the template: no `using` line, and the placeholder exactly once, on the constant's line.
pub fn check_template(s: &Source) -> Result<(), String> {
    let t = text(s)?;
    for (n, line) in t.lines().enumerate() {
        if let Using::Namespace(_) = using(line, &format!("{}:{}", s.path, n + 1))? {
            return Err(format!("{}:{}: a `using` directive in the template, which follows declarations", s.path, n + 1));
        }
    }
    let lines: Vec<&str> = t.lines().filter(|l| l.contains(PLACEHOLDER)).collect();
    if t.matches(PLACEHOLDER).count() != 1 || lines[0].trim() != CONSTANT_LINE {
        return Err(format!("{}: the placeholder must appear exactly once, as `{CONSTANT_LINE}`", s.path));
    }
    Ok(())
}

/// The template filled with the release hash (64 lowercase hex digits).
pub fn fill(template: &str, hash: &str) -> String {
    template.replacen(PLACEHOLDER, hash, 1)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn src(path: &str, t: &str) -> Source {
        Source { path: path.into(), bytes: t.as_bytes().to_vec() }
    }

    #[test]
    fn hoists_sorts_and_dedups_usings() {
        let a = src("model/A.cs", "// a\nusing System;\nusing Zed.Q;\nnamespace N\n{\n    class A { void F() { using(var x = G()) { } using var y = H(); } }\n}\n");
        let b = src("model/B.cs", "using System;\nusing Alpha;\n\nnamespace N { class B { } }\n");
        let out = body(&[&a, &b]).unwrap();
        let head: Vec<&str> = out.lines().filter(|l| l.starts_with("using ")).collect();
        assert_eq!(head, ["using Alpha;", "using System;", "using Zed.Q;"]);
        assert!(out.contains("// ---- model/A.cs ----\n// a\nnamespace N\n"));
        assert!(out.contains("using(var x = G())") && out.contains("using var y = H();"));
    }

    #[test]
    fn refuses_what_it_cannot_hoist_or_read() {
        for (t, why) in [
            ("namespace N { }\nusing System;\n", "after a declaration"),
            ("namespace N {\n    using System;\n}\n", "after a declaration"),
            ("using S = System.String;\nnamespace N { }\n", "alias"),
            ("using static System.Math;\nnamespace N { }\n", "using static"),
            ("global using System;\nnamespace N { }\n", "global using"),
            ("using System; // why\nnamespace N { }\n", "cannot read"),
            ("\u{feff}using System;\n", "byte-order mark"),
            ("using System;\r\n", "CR byte"),
            ("namespace N { }", "newline"),
        ] {
            let e = body(&[&src("model/X.cs", t)]).err().unwrap_or_default();
            assert!(e.contains(why), "{t:?}: {e}");
        }
    }

    #[test]
    fn a_cs_name_in_another_case_refuses() {
        let entries = [src("model/A.cs", ""), src("model/B.CS", "")];
        assert!(models(&entries).err().unwrap().contains("model/B.CS"));
    }

    #[test]
    fn the_template_must_carry_the_placeholder_once_on_its_line() {
        let ok = format!("namespace N\n{{\n    class R\n    {{\n        {CONSTANT_LINE}\n    }}\n}}\n");
        check_template(&src("t", &ok)).unwrap();
        for bad in [
            ok.replace("static readonly", "const"),
            ok.replace("namespace N", &format!("// {PLACEHOLDER}\nnamespace N")),
            format!("using System;\n{ok}"),
        ] {
            assert!(check_template(&src("t", &bad)).is_err(), "{bad}");
        }
        assert_eq!(fill("a @@MANIFEST_SHA256@@ b", "00ff"), "a 00ff b");
    }
}
