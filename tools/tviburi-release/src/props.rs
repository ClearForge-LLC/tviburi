//! The guards' constructor parameters (TVI-WO-2100 Q23 (B), ruled 2026-11-26T18:38Z): Renode binds a `.repl` entry's
//! properties to its type's public constructor parameters by name, so the parameter list, read from the guard's C#
//! source (the same `model/*.cs` the overlay concatenates), is the truth the MANIFEST's `property` lines must match.
//! Anything this cannot read exactly refuses by name.

/// The bus-peripheral interfaces: a class whose base list names one is a peripheral a platform declares.
pub const BUS_INTERFACES: [&str; 5] = ["IBusPeripheral", "IBytePeripheral", "IWordPeripheral", "IDoubleWordPeripheral", "IQuadWordPeripheral"];

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

const MODIFIERS: [&str; 8] = ["public ", "internal ", "private ", "protected ", "sealed ", "abstract ", "static ", "partial "];

fn ident(s: &str) -> bool {
    let mut c = s.chars();
    matches!(c.next(), Some(f) if f.is_ascii_alphabetic() || f == '_') && c.all(|x| x.is_ascii_alphanumeric() || x == '_')
}

/// Every class this file declares, each as (name, the names its base list gives, without namespace or type arguments).
/// A declaration is a line that begins, after its indentation and modifiers, with `class <Name>`; its base list runs to the
/// first `{`, on that line or a later one. A `class` anywhere else on a line (a comment, a string) is not a declaration.
pub fn classes(text: &str) -> Vec<(String, Vec<String>)> {
    let lines: Vec<&str> = text.lines().collect();
    let mut out = Vec::new();
    for (i, line) in lines.iter().enumerate() {
        let mut t = line.trim_start();
        while let Some(m) = MODIFIERS.iter().find(|m| t.starts_with(**m)) {
            t = &t[m.len()..];
        }
        let Some(rest) = t.strip_prefix("class ") else { continue };
        let end = rest.find(|c: char| !(c.is_ascii_alphanumeric() || c == '_')).unwrap_or(rest.len());
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
        let tail = tail.split('{').next().unwrap_or("").trim();
        let bases = match tail.strip_prefix(':') {
            Some(list) => split(list)
                .unwrap_or_default()
                .iter()
                .map(|b| {
                    let b = b.split('<').next().unwrap_or("").trim();
                    b.rsplit('.').next().unwrap_or("").to_string()
                })
                .collect(),
            None => vec![],
        };
        out.push((name.to_string(), bases));
    }
    out
}

/// Is a class with these bases a peripheral by its base list?
pub fn names_bus_interface(bases: &[String]) -> bool {
    bases.iter().any(|b| BUS_INTERFACES.contains(&b.as_str()))
}

/// Split at commas outside brackets of any kind and outside string and character literals.
fn split(s: &str) -> Result<Vec<String>, String> {
    let (mut out, mut cur, mut depth, mut quote, mut esc) = (Vec::new(), String::new(), 0i32, None::<char>, false);
    for c in s.chars() {
        if let Some(q) = quote {
            cur.push(c);
            if esc {
                esc = false;
            } else if c == '\\' {
                esc = true;
            } else if c == q {
                quote = None;
            }
            continue;
        }
        match c {
            '"' | '\'' => quote = Some(c),
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
    if quote.is_some() || depth != 0 {
        return Err("an unterminated literal or bracket".into());
    }
    let last = cur.trim().to_string();
    if !(last.is_empty() && out.is_empty()) {
        out.push(last);
    }
    Ok(out)
}

/// The public constructor of `class`, which must be exactly one, as the properties a platform gives it: every parameter but
/// a first `IMachine` or `Machine` one, which Renode supplies itself.
pub fn constructor(path: &str, text: &str, class: &str) -> Result<Vec<Param>, String> {
    let head = format!("public {class}(");
    let starts: Vec<usize> = text
        .match_indices(&head)
        .map(|(i, _)| i)
        .filter(|&i| text[..i].rsplit('\n').next().is_some_and(|before| before.trim().is_empty()))
        .collect();
    let [start] = starts.as_slice() else {
        return Err(format!("{path}: {class} has {} public constructors, not one: its properties cannot be read from one parameter list", starts.len()));
    };
    let open = start + head.len();
    let (mut depth, mut quote, mut esc, mut close) = (1, None::<char>, false, None);
    for (i, c) in text[open..].char_indices() {
        if let Some(q) = quote {
            if esc {
                esc = false;
            } else if c == '\\' {
                esc = true;
            } else if c == q {
                quote = None;
            }
            continue;
        }
        match c {
            '"' | '\'' => quote = Some(c),
            '(' => depth += 1,
            ')' => {
                depth -= 1;
                if depth == 0 {
                    close = Some(open + i);
                    break;
                }
            }
            _ => {}
        }
    }
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

    #[test]
    fn reads_the_declarations_and_their_base_lists_and_only_bus_peripherals_are_peripherals() {
        let c = classes(FILE);
        let got: Vec<(&str, bool)> = c.iter().map(|(n, b)| (n.as_str(), names_bus_interface(b))).collect();
        assert_eq!(got, [("GuardedA", true), ("KernelClock", false), ("Tap", false), ("GuardedB", true), ("Records", false)]);
        assert_eq!(c[0].1, ["IDoubleWordPeripheral", "IKnownSize", "IDisposable", "IRegisterablePeripheral"], "a base list over two lines");
        assert_eq!(c[3].1, ["SimpleContainer", "IWordPeripheral"], "type arguments and a namespace dropped");
    }

    #[test]
    fn reads_the_one_public_constructor_over_lines_with_type_arguments_and_skips_machine_only_first() {
        let a = constructor("f", FILE, "GuardedA").unwrap();
        assert_eq!(names(&a), [("systick", true), ("hse", true), ("pins", true), ("mode", true), ("limit", false)]);
        assert_eq!(names(&constructor("f", FILE, "GuardedB").unwrap()), [("n", false)], "`Machine machine` is Renode's");
        assert_eq!(names(&constructor("f", FILE, "KernelClock").unwrap()), [("peripheral", false), ("modeled", false)]);
        let r = constructor("f", FILE, "Records").unwrap();
        assert_eq!(names(&r), [("owner", false), ("guard", false), ("mode", false), ("witness", true)], "a comma inside <> is not a parameter's end");
        let not_first = FILE.replace("GuardedB(Machine machine, int n)", "GuardedB(int n, Machine machine)");
        assert_eq!(names(&constructor("f", &not_first, "GuardedB").unwrap()), [("n", false), ("machine", false)], "machine only as the first");
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
            let e = constructor("f", &FILE.replace(from, to), "GuardedB").err().unwrap_or_default();
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
