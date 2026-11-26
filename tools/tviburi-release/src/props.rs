//! The guards' constructor parameters (TVI-WO-2100 Q23 (B), ruled 2026-11-26T18:38Z): Renode binds a `.repl` entry's
//! properties to its type's public constructor parameters by name, so the parameter list, read from the guard's C#
//! source (the same `model/*.cs` the overlay concatenates), is the truth the MANIFEST's `property` lines must match.
//! Anything this cannot read exactly refuses by name.

/// One public constructor parameter, as a `.repl` property sees it.
#[derive(Debug, Clone, PartialEq)]
pub struct Param {
    pub name: String,
    /// It has a C# default value, so a platform without it still loads.
    pub has_default: bool,
}

/// One `property` row of pins.tsv.
#[derive(Debug, Clone, PartialEq)]
pub struct Prop {
    pub guard: String,
    pub name: String,
    pub required: bool,
    pub meaning: String,
}

const MODIFIERS: [&str; 10] = ["public", "internal", "private", "protected", "sealed", "abstract", "static", "partial", "unsafe", "new"];

fn ident(s: &str) -> bool {
    let mut c = s.chars();
    matches!(c.next(), Some(f) if f.is_ascii_alphabetic() || f == '_') && c.all(|x| x.is_ascii_alphanumeric() || x == '_')
}

/// The text with every comment (`//`, `/* */`) blanked and the inside of every string and character literal (regular,
/// verbatim `@"..."`, interpolated `$"..."`; escapes, and `""` in a verbatim one) replaced by `_`, newlines kept, so that
/// nothing in a comment or a literal reads as code and every line keeps its number. An unterminated literal or comment
/// refuses.
pub fn mask(path: &str, text: &str) -> Result<String, String> {
    let c: Vec<char> = text.chars().collect();
    let (n, mut i, mut out) = (c.len(), 0, String::with_capacity(text.len()));
    let at = |k: usize| c.get(k).copied().unwrap_or('\0');
    let blank = |x: char| if x == '\n' { '\n' } else { ' ' };
    while i < n {
        if c[i] == '/' && at(i + 1) == '/' {
            while i < n && c[i] != '\n' {
                out.push(' ');
                i += 1;
            }
        } else if c[i] == '/' && at(i + 1) == '*' {
            let start = i;
            out.push_str("  ");
            i += 2;
            while i < n && !(c[i] == '*' && at(i + 1) == '/') {
                out.push(blank(c[i]));
                i += 1;
            }
            if i >= n {
                return Err(format!("{path}: a `/*` comment at character {start} never closes"));
            }
            out.push_str("  ");
            i += 2;
        } else if c[i] == '"' || (matches!(c[i], '@' | '$') && (at(i + 1) == '"' || (matches!(at(i + 1), '@' | '$') && at(i + 2) == '"'))) {
            let start = i;
            let mut verbatim = false;
            while c[i] != '"' {
                verbatim |= c[i] == '@';
                out.push(c[i]);
                i += 1;
            }
            out.push('"');
            i += 1;
            loop {
                if i >= n || (!verbatim && c[i] == '\n') {
                    return Err(format!("{path}: a string literal at character {start} never closes"));
                }
                if c[i] == '"' && verbatim && at(i + 1) == '"' {
                    out.push_str("__");
                    i += 2;
                } else if c[i] == '"' {
                    out.push('"');
                    i += 1;
                    break;
                } else if c[i] == '\\' && !verbatim {
                    out.push_str("__");
                    i += 2;
                } else {
                    out.push(if c[i] == '\n' { '\n' } else { '_' });
                    i += 1;
                }
            }
        } else if c[i] == '\'' {
            let start = i;
            out.push('\'');
            i += 1;
            while i < n && c[i] != '\'' && c[i] != '\n' {
                if c[i] == '\\' && i + 1 < n {
                    out.push('_');
                    i += 1;
                }
                out.push('_');
                i += 1;
            }
            if i >= n || c[i] != '\'' {
                return Err(format!("{path}: a character literal at character {start} never closes"));
            }
            out.push('\'');
            i += 1;
        } else {
            out.push(c[i]);
            i += 1;
        }
    }
    Ok(out)
}

/// A type's name without its namespace or type arguments: `Bus.IWordPeripheral` and `SimpleContainer<II2CPeripheral>` are
/// `IWordPeripheral` and `SimpleContainer`.
pub fn bare(t: &str) -> String {
    t.split('<').next().unwrap_or("").trim().rsplit('.').next().unwrap_or("").to_string()
}

/// Every class this (masked) text declares, each as (name, its base list's names, bare). A declaration is a line that is,
/// after its indentation, any `[...]` attributes and any modifiers, `class <Name>`, with or without type parameters; its base
/// list runs from a `:` to the first `{` or `where`, on that line or a later one.
pub fn classes(text: &str) -> Vec<(String, Vec<String>)> {
    let lines: Vec<&str> = text.lines().collect();
    let mut out = Vec::new();
    for (i, line) in lines.iter().enumerate() {
        let mut t = line.trim_start();
        while t.starts_with('[') {
            let mut depth = 0;
            let Some(end) = t.char_indices().find_map(|(k, ch)| {
                depth += match ch { '[' => 1, ']' => -1, _ => 0 };
                (depth == 0).then_some(k)
            }) else { break };
            t = t[end + 1..].trim_start();
        }
        loop {
            let word = t.split(|ch: char| !(ch.is_ascii_alphanumeric() || ch == '_')).next().unwrap_or("");
            if !MODIFIERS.contains(&word) || !t[word.len()..].starts_with(char::is_whitespace) {
                break;
            }
            t = t[word.len()..].trim_start();
        }
        let Some(rest) = t.strip_prefix("class ") else { continue };
        let rest = rest.trim_start();
        let end = rest.find(|ch: char| !(ch.is_ascii_alphanumeric() || ch == '_')).unwrap_or(rest.len());
        let name = &rest[..end];
        if !ident(name) {
            continue;
        }
        let mut tail = rest[end..].to_string();
        for l in &lines[i + 1..] {
            if tail.contains('{') {
                break;
            }
            tail.push(' ');
            tail.push_str(l);
        }
        let mut tail = tail.split('{').next().unwrap_or("").trim();
        if tail.starts_with('<') {
            let mut depth = 0;
            let end = tail.char_indices().find_map(|(k, ch)| {
                depth += match ch { '<' => 1, '>' => -1, _ => 0 };
                (depth == 0).then_some(k)
            });
            tail = end.map_or("", |e| tail[e + 1..].trim());
        }
        let bases = match tail.strip_prefix(':') {
            Some(list) => {
                let list = list.split(" where ").next().unwrap_or("");
                split(list).unwrap_or_default().iter().map(|b| bare(b)).collect()
            }
            None => vec![],
        };
        out.push((name.to_string(), bases));
    }
    out
}

/// The peripherals among the declared classes, each (name, bases): every class in `mapped` (the class map's), and every class
/// whose base list names a type with `Peripheral` in its name (`IDoubleWordPeripheral`, `BasicDoubleWordPeripheral`, ...), a
/// stock peripheral type (`stock`: the class map's stock types and the pinned chain's, bare), or a peripheral class of the
/// overlay itself, to any depth.
pub fn peripheral_names(declared: &[(String, Vec<String>)], mapped: &[String], stock: &[String]) -> Vec<String> {
    let mut out: Vec<String> = Vec::new();
    loop {
        let before = out.len();
        for (name, bases) in declared {
            let is = mapped.contains(name)
                || bases.iter().any(|b| b.contains("Peripheral") || stock.contains(b) || out.contains(b));
            if is && !out.contains(name) {
                out.push(name.clone());
            }
        }
        if out.len() == before {
            return out;
        }
    }
}

/// Split at commas outside brackets of any kind (literals are already masked).
fn split(s: &str) -> Result<Vec<String>, String> {
    let (mut out, mut cur, mut depth) = (Vec::new(), String::new(), 0i32);
    for c in s.chars() {
        match c {
            '(' | '<' | '[' | '{' => depth += 1,
            ')' | '>' | ']' | '}' => depth -= 1,
            ',' if depth == 0 => {
                out.push(std::mem::take(&mut cur).trim().to_string());
                continue;
            }
            _ => {}
        }
        if depth < 0 {
            return Err("unbalanced brackets".into());
        }
        cur.push(c);
    }
    if depth != 0 {
        return Err("an unbalanced bracket".into());
    }
    let last = cur.trim().to_string();
    if !(last.is_empty() && out.is_empty()) {
        out.push(last);
    }
    Ok(out)
}

/// The public constructor of `class`, which must be exactly one, in `text` masked by [`mask`], as the properties a platform
/// gives it: every parameter but a first `IMachine` or `Machine` one, which Renode supplies itself.
pub fn constructor(path: &str, text: &str, class: &str) -> Result<Vec<Param>, String> {
    let head = format!("public {class}(");
    // The text is masked, so no comment or literal holds the head: each one is a constructor of `class`.
    let starts: Vec<usize> = text.match_indices(&head).map(|(i, _)| i).collect();
    let [start] = starts.as_slice() else {
        return Err(format!("{path}: {class} has {} public constructors, not one: its properties cannot be read from one parameter list", starts.len()));
    };
    let open = start + head.len();
    let mut depth = 1;
    let close = text[open..].char_indices().find_map(|(i, c)| {
        depth += match c { '(' => 1, ')' => -1, _ => 0 };
        (depth == 0).then_some(open + i)
    });
    let close = close.ok_or(format!("{path}: {class}'s constructor's parameter list never closes"))?;
    let list = split(&text[open..close]).map_err(|e| format!("{path}: {class}'s constructor's parameters: {e}"))?;
    let mut params = Vec::new();
    for (k, p) in list.iter().enumerate() {
        let p = p.split_whitespace().collect::<Vec<_>>().join(" ");
        if p.starts_with('[') || ["params ", "ref ", "out ", "in ", "this "].iter().any(|m| p.starts_with(m)) {
            return Err(format!("{path}: {class}'s constructor parameter `{p}`: an attribute or modifier this release does not read as a property"));
        }
        let (decl, has_default) = match p.split_once('=') {
            Some((d, v)) if !v.trim().is_empty() => (d.trim(), true),
            Some(_) => return Err(format!("{path}: {class}'s constructor parameter `{p}` has an empty default")),
            None => (p.as_str(), false),
        };
        let words: Vec<&str> = decl.rsplitn(2, ' ').collect();
        let [name, ty] = words.as_slice() else {
            return Err(format!("{path}: {class}'s constructor parameter `{p}` is not `<type> <name>`"));
        };
        if !ident(name) {
            return Err(format!("{path}: {class}'s constructor parameter `{p}`: `{name}` is not a name"));
        }
        if k == 0 && (*ty == "IMachine" || *ty == "Machine") {
            continue;
        }
        params.push(Param { name: name.to_string(), has_default });
    }
    Ok(params)
}

/// The `property` rows against the peripherals' parameters, each (guard type, in the class map, parameters). Refuses, by name:
/// a row twice; a row naming no parameter of a peripheral (R2); a row `optional` for a parameter with no default; a parameter
/// with no row, as the class map's guard (R3) or as another peripheral `Tviburi.` class (R1).
pub fn check(peripherals: &[(String, bool, Vec<Param>)], rows: &[Prop]) -> Result<(), String> {
    for (i, r) in rows.iter().enumerate() {
        if rows[..i].iter().any(|o| o.guard == r.guard && o.name == r.name) {
            return Err(format!("pins.tsv: the property row {} {} twice", r.guard, r.name));
        }
        let param = peripherals.iter().filter(|p| p.0 == r.guard).flat_map(|p| p.2.iter()).find(|p| p.name == r.name);
        let Some(param) = param else {
            return Err(format!("pins.tsv: the property row {} {} names no public constructor parameter of a peripheral Tviburi class", r.guard, r.name));
        };
        if !r.required && !param.has_default {
            return Err(format!("pins.tsv: the property row {} {} is optional, but the parameter has no C# default, so a platform without it does not load: it is required", r.guard, r.name));
        }
    }
    for (guard, mapped, params) in peripherals {
        for p in params {
            if !rows.iter().any(|r| &r.guard == guard && r.name == p.name) {
                return Err(if *mapped {
                    format!("{guard}, a guard of the class map: its constructor parameter {} has no property row in pins.tsv", p.name)
                } else {
                    format!("{guard}, a peripheral Tviburi class: its public constructor parameter {} has no property row in pins.tsv", p.name)
                });
            }
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    const FILE: &str = r#"
namespace Antmicro.Renode.Peripherals.Tviburi
{
    public interface IRccWatched : IPeripheral
    {
    }

    public class GuardedA : IDoubleWordPeripheral, IKnownSize, IDisposable,
        IRegisterablePeripheral<IPeripheral, EnumRegistrationPoint<STM32H7_RCC.RegistrationPeripheralAlias>>
    {
        // the class map's guard: "class Fake : IDoubleWordPeripheral" in a comment is no declaration
        public GuardedA(IMachine machine, IHasDivisibleFrequency systick = null, ulong? hse = null,
            List<List<int>> pins = null, string mode = Records.Enforcing, uint limit)
        {
            var s = "public class Spoof : IBytePeripheral";
        }

        public sealed class KernelClock
        {
            public KernelClock(string peripheral, bool modeled)
            {
            }
        }

        private sealed class Tap : IGPIOReceiver
        {
            public Tap(GuardedA parent, int channel) { }
        }
    }

    public class GuardedB : SimpleContainer<II2CPeripheral>, Bus.IWordPeripheral
    {
        public GuardedB(Machine machine, int n) : base(machine) { }
    }

    public sealed class Records
    {
        public Records(IMachine machine, IPeripheral owner, string guard, string mode, IEnumerable<KeyValuePair<string, string>> witness = null)
        {
        }
    }
}
"#;

    fn names(ps: &[Param]) -> Vec<(&str, bool)> {
        ps.iter().map(|p| (p.name.as_str(), p.has_default)).collect()
    }

    fn m(t: &str) -> String {
        mask("f", t).unwrap()
    }

    #[test]
    fn reads_the_declarations_and_their_base_lists_with_comments_and_literals_masked() {
        let c = classes(&m(FILE));
        let got: Vec<&str> = c.iter().map(|(n, _)| n.as_str()).collect();
        assert_eq!(got, ["GuardedA", "KernelClock", "Tap", "GuardedB", "Records"], "no Fake (a comment), no Spoof (a string)");
        assert_eq!(c[0].1, ["IDoubleWordPeripheral", "IKnownSize", "IDisposable", "IRegisterablePeripheral"], "a base list over two lines");
        assert_eq!(c[3].1, ["SimpleContainer", "IWordPeripheral"], "type arguments and a namespace dropped");
        assert_eq!(peripheral_names(&c, &[], &[]), ["GuardedA", "GuardedB"], "a Peripheral base; Records, KernelClock and Tap are not");
    }

    #[test]
    fn a_peripheral_is_read_whatever_its_attributes_modifiers_type_parameters_or_peripheral_base() {
        // The adversarial pass's four forms, a stock base, a base of the overlay, and the controls that take no rows.
        let t = m(r#"
    public class Basic : BasicDoubleWordPeripheral { }
    public class Generic<T> : IBytePeripheral where T : class { }
    [Icon("x")] [Other(1, "]")] public class Attributed : IBytePeripheral { }
    public unsafe class Unsafe : IBytePeripheral { }
    internal sealed partial class Stock : STM32_Timer, IDisposable { }
    public class Derived : Stock { }
    public class DerivedTwice : Derived { }
    public sealed class Records { }
    public static class TviburiRelease { }
    private sealed class Tap : IGPIOReceiver { }
    public class Mapped { }
"#);
        let c = classes(&t);
        assert_eq!(c.len(), 11, "{c:?}");
        assert_eq!(c[1].1, ["IBytePeripheral"], "type parameters and the where clause are not bases");
        let mapped = vec!["Mapped".to_string()];
        let got = peripheral_names(&c, &mapped, &["STM32_Timer".to_string()]);
        let mut got = got.iter().map(String::as_str).collect::<Vec<_>>();
        got.sort();
        assert_eq!(got, ["Attributed", "Basic", "Derived", "DerivedTwice", "Generic", "Mapped", "Stock", "Unsafe"]);
    }

    #[test]
    fn masks_comments_and_every_literal_so_no_comma_quote_or_comment_moves_a_parameter() {
        // The adversarial pass's plant: an apostrophe inside a comment opened a "literal" that swallowed `extra`.
        let t = "public P(IMachine machine, string mode = Records.Enforcing /* the guard's */, string extra = null /* the part's */, string revision = null)\n";
        assert_eq!(names(&constructor("f", &m(t), "P").unwrap()), [("mode", true), ("extra", true), ("revision", true)]);
        let t = "public P(Machine machine, string s = \"a,b\", // it's (one)\n    string v = @\"c:\\x\"\",y\", char c = ',', char q = '\\'', List<List<int>> x = null, int k = F(1, 2))\n";
        assert_eq!(names(&constructor("f", &m(t), "P").unwrap()), [("s", true), ("v", true), ("c", true), ("q", true), ("x", true), ("k", true)]);
        let t = "a\n/* x\ny */ b @\"q\nr\" 'z'\nc // d\n";
        assert_eq!(m(t).lines().count(), t.lines().count(), "newlines kept");
        for (bad, why) in [("\"open", "string literal"), ("/* open", "comment"), ("'x", "character literal"), ("\"a\nb\"", "string literal")] {
            assert!(mask("f", bad).unwrap_err().contains(why), "{bad}");
        }
    }

    #[test]
    fn reads_the_one_public_constructor_over_lines_with_type_arguments_and_skips_machine_only_first() {
        let f = m(FILE);
        let a = constructor("f", &f, "GuardedA").unwrap();
        assert_eq!(names(&a), [("systick", true), ("hse", true), ("pins", true), ("mode", true), ("limit", false)]);
        assert_eq!(names(&constructor("f", &f, "GuardedB").unwrap()), [("n", false)], "`Machine machine` is Renode's");
        assert_eq!(names(&constructor("f", &f, "KernelClock").unwrap()), [("peripheral", false), ("modeled", false)]);
        let r = constructor("f", &f, "Records").unwrap();
        assert_eq!(names(&r), [("owner", false), ("guard", false), ("mode", false), ("witness", true)], "a comma inside <> is not a parameter's end");
        let not_first = m(&FILE.replace("GuardedB(Machine machine, int n)", "GuardedB(int n, Machine machine)"));
        assert_eq!(names(&constructor("f", &not_first, "GuardedB").unwrap()), [("n", false), ("machine", false)], "machine only as the first");
        let commented = m(&FILE.replace("public GuardedB(Machine machine, int n) : base(machine) { }", "// public GuardedB(Machine machine) : base(machine) { }\n        public GuardedB(Machine machine, int n) : base(machine) { }"));
        assert_eq!(names(&constructor("f", &commented, "GuardedB").unwrap()), [("n", false)], "a commented-out constructor is none");
    }

    #[test]
    fn refuses_a_constructor_it_cannot_read_as_one_list_of_properties() {
        for (from, to, why) in [
            ("public GuardedB(Machine machine, int n) : base(machine) { }", "", "0 public constructors"),
            ("public GuardedB(Machine machine, int n) : base(machine) { }", "public GuardedB(Machine machine, int n) : base(machine) { }\n        public GuardedB(Machine machine) : base(machine) { }", "2 public constructors"),
            ("Machine machine, int n)", "Machine machine, ref int n)", "modifier"),
            ("Machine machine, int n)", "Machine machine, params int[] n)", "modifier"),
            ("Machine machine, int n)", "Machine machine, int n = )", "empty default"),
            ("Machine machine, int n)", "Machine machine, n)", "not `<type> <name>`"),
            ("public GuardedB(Machine machine, int n) : base(machine) { }", "public GuardedB(Machine machine, int n", "never closes"),
        ] {
            assert_eq!(FILE.matches(from).count(), 1, "{from}");
            let e = constructor("f", &m(&FILE.replace(from, to)), "GuardedB").err().unwrap_or_default();
            assert!(e.contains(why), "{to}: {e}");
        }
    }

    fn prop(guard: &str, name: &str, required: bool) -> Prop {
        Prop { guard: guard.into(), name: name.into(), required, meaning: "m".into() }
    }

    #[test]
    fn every_parameter_has_one_row_every_row_one_parameter_and_no_default_is_required() {
        let p = |n: &str, d: bool| Param { name: n.into(), has_default: d };
        let peripherals = vec![
            ("Tviburi.GuardedA".to_string(), true, vec![p("mode", true), p("limit", false)]),
            ("Tviburi.Extra".to_string(), false, vec![p("x", true)]),
        ];
        let good = vec![prop("Tviburi.GuardedA", "mode", false), prop("Tviburi.GuardedA", "limit", true), prop("Tviburi.Extra", "x", false)];
        check(&peripherals, &good).unwrap();
        // R3: a guard of the class map, one of its parameters with no row.
        assert_eq!(check(&peripherals, &good[1..]).unwrap_err(), "Tviburi.GuardedA, a guard of the class map: its constructor parameter mode has no property row in pins.tsv");
        // R1: a peripheral Tviburi class the class map does not name, its parameter with no row.
        assert_eq!(check(&peripherals, &good[..2]).unwrap_err(), "Tviburi.Extra, a peripheral Tviburi class: its public constructor parameter x has no property row in pins.tsv");
        // R2: a row naming no parameter: a name the guard does not take, and a class that is no peripheral (Records).
        for bad in [prop("Tviburi.GuardedA", "modes", false), prop("Tviburi.Records", "mode", true)] {
            let rows: Vec<Prop> = good.iter().cloned().chain([bad.clone()]).collect();
            assert_eq!(check(&peripherals, &rows).unwrap_err(), format!("pins.tsv: the property row {} {} names no public constructor parameter of a peripheral Tviburi class", bad.guard, bad.name));
        }
        // A parameter with no C# default declared optional.
        let optional = vec![good[0].clone(), prop("Tviburi.GuardedA", "limit", false), good[2].clone()];
        assert!(check(&peripherals, &optional).unwrap_err().contains("GuardedA limit is optional, but the parameter has no C# default"));
        // Twice.
        let twice: Vec<Prop> = good.iter().cloned().chain([good[0].clone()]).collect();
        assert!(check(&peripherals, &twice).unwrap_err().contains("GuardedA mode twice"));
    }
}
