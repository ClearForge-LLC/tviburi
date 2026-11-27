//! `tviburi-route`: TVI-WO-1202's CI route, shard plan and aggregate (`RUL-14`: a gate's logic is a program).
//!
//!   classify CHANGED            one path per line (`git diff --no-renames --name-only`). Prints `class=docs` only if
//!                               every path is documentation, else `class=code`; an empty list is code. Prints
//!                               `budget=yes` if any path is the memory budget's code.
//!   inventory LS CITATIONS      over the checkout in the working directory (LS: `git ls-files`): fails if a path
//!                               classed as documentation is not Markdown, if a code file names a documentation path
//!                               other than as a listed citation, or if the budget's scripts read outside its list.
//!   plan N K TIMES CASES MUTANTS
//!                               shard K of N: every case (CASES, the suite's own list, in order) and every mutant
//!                               (MUTANTS, `tests/mutants.tsv`) packed longest first by measured time (TIMES); an item
//!                               with no time weighs the median of its kind. Prints the shard's items in suite order.
//!   aggregate CASES MUTANTS RAN...
//!                               each RAN is one shard's record of what it ran; fails unless every case and mutant
//!                               of the tree ran exactly once across them, and nothing else did.
//! Exit 0: pass. Exit 1: a check failed (FAIL lines). Exit 2: usage or an unreadable input.
use std::collections::{BTreeMap, BTreeSet};
use std::process::ExitCode;

/// The documentation at the repository's root (ruled 2026-11-24T10:30Z, Q3). Everything else outside `docs/**/*.md`
/// is code.
const ROOT_DOCS: [&str; 5] = ["README.md", "FEEDBACK.md", "CLAUDE.md", "LICENSE-MIT", "LICENSE-APACHE"];
/// The three Markdown files the release reads (the acknowledgements' homes, whose A-rows `tviburi-release` checks):
/// a document the release reads is a source, so each routes as code (ruled 2026-11-27T00:10Z, TVI-WO-1206 (A)).
const READ_DOCS: [&str; 3] = [
    "docs/work-orders/TVI-WO-0100b.rm0433.md",
    "docs/work-orders/TVI-WO-1300.rm0433.md",
    "docs/work-orders/TVI-WO-1206.rm0433.md",
];
/// The memory budget's code: a change here runs the budget's own tests on a pull request (ruled 10:30Z, Q2). A
/// trailing `/` is a directory.
const BUDGET: [&str; 7] = [
    "tests/1201/",
    "tests/1301/",
    "tests/spike.sh",
    "tests/ci/",
    "tools/tviburi-route/",
    "tools/tviburi-budget/",
    ".github/workflows/",
];
/// The scripts the budget's tests run; the inventory reads every `$here/<path>` they name.
const BUDGET_READERS: [&str; 3] = ["tests/1201/test-budget.sh", "tests/1301/ci-memory.sh", "tests/1301/replay-1301.sh"];

fn is_docs(p: &str) -> bool {
    !p.split('/').any(|c| c.is_empty() || c == "." || c == "..")
        && (ROOT_DOCS.contains(&p) || (p.starts_with("docs/") && p.ends_with(".md") && !READ_DOCS.contains(&p)))
}

fn under(list: &[&str], p: &str) -> bool {
    list.iter().any(|b| if b.ends_with('/') { p.starts_with(b) } else { p == *b })
}

/// (docs, budget) for a diff: docs only if it is not empty and every path is documentation.
fn classify(paths: &[&str]) -> (bool, bool) {
    (!paths.is_empty() && paths.iter().all(|p| is_docs(p)), paths.iter().any(|p| under(&BUDGET, p)))
}

fn lines(text: &str) -> Vec<&str> {
    text.lines().map(|l| l.trim_end_matches('\r')).filter(|l| !l.is_empty()).collect()
}

/// A symlink's target as a repository path, resolved lexically from the link's own directory; None if it leaves the
/// repository.
fn resolve(link: &str, target: &str) -> Option<String> {
    let mut parts: Vec<&str> = if target.starts_with('/') { return None } else { link.split('/').collect() };
    parts.pop();
    for c in target.split('/') {
        match c {
            "" | "." => {}
            ".." => { parts.pop()?; }
            c => parts.push(c),
        }
    }
    Some(parts.join("/"))
}

/// The inventory's failures. `files`: every tracked path with its bytes; `links`: every tracked symlink and its
/// target as written; `citations`: (file, token) pairs that name documentation in a code file as a citation, not a
/// read; `budget`: the budget's list.
fn inventory(files: &[(String, Vec<u8>)], links: &[(String, String)], citations: &BTreeSet<(String, String)>, docs: fn(&str) -> bool, budget: &[&str]) -> Vec<String> {
    let mut fails = Vec::new();
    // 0. No symlink is documentation, and none reaches documentation from code: a docs-only change would change what
    //    the code reads.
    for (l, t) in links {
        let to = resolve(l, t);
        if docs(l) || to.as_deref().map_or(true, |p| docs(p) || p == "docs" || p.starts_with("docs/")) {
            fails.push(format!("{l}: a symlink to `{t}`, which is or reaches documentation (or leaves the repository)"));
        }
    }
    // 1. Documentation is Markdown, or one of the two licence files at the root.
    for (p, _) in files.iter().filter(|(p, _)| docs(p)) {
        if !(p.ends_with(".md") || p == "LICENSE-MIT" || p == "LICENSE-APACHE") {
            fails.push(format!("{p}: classed as documentation, but it is not Markdown: a change to it would skip the suite"));
        }
    }
    // 2. No code file names a documentation file, or `docs/`, except as a listed citation.
    let mut tokens: BTreeSet<String> = files.iter().filter(|(p, _)| docs(p)).map(|(p, _)| p.rsplit('/').next().unwrap().to_string()).collect();
    tokens.insert("docs/".into());
    let mut found = BTreeSet::new();
    for (p, bytes) in files.iter().filter(|(p, _)| !docs(p) && p != "tests/ci/citations.tsv") {
        for t in &tokens {
            if bytes.windows(t.len()).any(|w| w == t.as_bytes()) {
                found.insert((p.clone(), t.clone()));
            }
        }
    }
    for (p, t) in found.difference(citations) {
        fails.push(format!("{p} names `{t}`, which is documentation: if it reads it, the path is code; if it cites it, list it in tests/ci/citations.tsv"));
    }
    for (p, t) in citations.difference(&found) {
        fails.push(format!("tests/ci/citations.tsv lists {p} naming `{t}`, which it no longer does"));
    }
    // 3. The budget's tests read only the budget's code.
    for r in BUDGET_READERS {
        if !under(budget, r) {
            fails.push(format!("{r}: a script of the budget's tests, outside the budget's list"));
        }
        let Some((_, bytes)) = files.iter().find(|(p, _)| p == r) else {
            fails.push(format!("{r}: a script of the budget's tests, not in the checkout"));
            continue;
        };
        let text = String::from_utf8_lossy(bytes);
        for (i, _) in text.match_indices("$here/") {
            let rest = &text[i + 6..];
            let end = rest.find(|c: char| !(c.is_ascii_alphanumeric() || "_./-".contains(c))).unwrap_or(rest.len());
            let path = rest[..end].trim_end_matches('/');
            if !path.is_empty() && !under(budget, path) && !under(budget, &format!("{path}/")) {
                fails.push(format!("{r} reads $here/{path}, outside the budget's list: a change there would not run the budget's tests"));
            }
        }
    }
    fails
}

/// The tree's items, in suite order: every case, then every mutant. Fails on an id twice or a malformed list.
fn items(cases: &str, mutants: &str) -> Result<Vec<(String, String)>, String> {
    let mut out: Vec<(String, String)> = Vec::new();
    for c in lines(cases) {
        if c.contains(char::is_whitespace) {
            return Err(format!("case list: `{c}` is not one id"));
        }
        out.push(("case".into(), c.into()));
    }
    for row in lines(mutants) {
        if row.starts_with('#') {
            continue;
        }
        out.push(("mutant".into(), row.split('\t').next().unwrap().into()));
    }
    let mut seen = BTreeSet::new();
    for i in &out {
        if !seen.insert(i) {
            return Err(format!("{} {} is listed twice in the tree", i.0, i.1));
        }
    }
    Ok(out)
}

/// Shard `k` (1-based) of `n`: longest first onto the least-loaded shard (ties: suite order, lowest shard).
fn plan(items: &[(String, String)], times: &BTreeMap<(String, String), f64>, n: usize, k: usize) -> Vec<(String, String)> {
    let median = |kind: &str| {
        let mut v: Vec<f64> = times.iter().filter(|((t, _), _)| t == kind).map(|(_, s)| *s).collect();
        v.sort_by(f64::total_cmp);
        if v.is_empty() { 1.0 } else { v[v.len() / 2] }
    };
    let (mc, mm) = (median("case"), median("mutant"));
    let w = |i: &(String, String)| times.get(i).copied().unwrap_or(if i.0 == "case" { mc } else { mm });
    let mut order: Vec<usize> = (0..items.len()).collect();
    order.sort_by(|&a, &b| w(&items[b]).total_cmp(&w(&items[a])).then(a.cmp(&b)));
    let mut load = vec![0.0f64; n];
    let mut shard = vec![0usize; items.len()];
    for i in order {
        let s = (0..n).min_by(|&a, &b| load[a].total_cmp(&load[b]).then(a.cmp(&b))).unwrap();
        load[s] += w(&items[i]);
        shard[i] = s;
    }
    items.iter().zip(shard).filter(|(_, s)| *s + 1 == k).map(|(i, _)| i.clone()).collect()
}

/// Every expected item exactly once across the shards' records, and nothing unexpected.
fn aggregate(expected: &[(String, String)], ran: &[Vec<(String, String)>]) -> Vec<String> {
    let mut count: BTreeMap<&(String, String), usize> = expected.iter().map(|i| (i, 0)).collect();
    let mut fails = Vec::new();
    for (s, r) in ran.iter().enumerate() {
        for i in r {
            match count.get_mut(i) {
                Some(c) => *c += 1,
                None => fails.push(format!("shard {} ran {} {}, which is not in the tree's lists", s + 1, i.0, i.1)),
            }
        }
    }
    for (i, c) in count {
        match c {
            1 => {}
            0 => fails.push(format!("{} {} did not run in any shard", i.0, i.1)),
            c => fails.push(format!("{} {} ran {c} times", i.0, i.1)),
        }
    }
    fails
}

fn record(text: &str) -> Result<Vec<(String, String)>, String> {
    lines(text)
        .into_iter()
        .map(|l| match l.split_once(' ') {
            Some((k @ ("case" | "mutant"), id)) if !id.contains(char::is_whitespace) => Ok((k.to_string(), id.to_string())),
            _ => Err(format!("`{l}` is not `case <id>` or `mutant <id>`")),
        })
        .collect()
}

fn read(p: &str) -> Result<String, String> {
    std::fs::read_to_string(p).map_err(|e| format!("{p}: {e}"))
}

fn run(a: &[String]) -> Result<bool, String> {
    let a: Vec<&str> = a.iter().map(String::as_str).collect();
    match a.as_slice() {
        ["classify", changed] => {
            let text = read(changed)?;
            let (docs, budget) = classify(&lines(&text));
            println!("class={}", if docs { "docs" } else { "code" });
            println!("budget={}", if budget { "yes" } else { "no" });
            Ok(true)
        }
        ["inventory", ls, citations] => {
            let (mut files, mut links) = (Vec::new(), Vec::new());
            for p in lines(&read(ls)?) {
                let meta = std::fs::symlink_metadata(p).map_err(|e| format!("{p}: {e}"))?;
                if meta.file_type().is_symlink() {
                    let t = std::fs::read_link(p).map_err(|e| format!("{p}: {e}"))?;
                    links.push((p.to_string(), t.to_string_lossy().into_owned()));
                    continue;
                }
                files.push((p.to_string(), std::fs::read(p).map_err(|e| format!("{p}: {e}"))?));
            }
            let mut cites = BTreeSet::new();
            for row in lines(&read(citations)?).into_iter().filter(|r| !r.starts_with('#')) {
                let (f, t) = row.split_once('\t').ok_or(format!("{citations}: `{row}` is not FILE<tab>TOKEN"))?;
                cites.insert((f.to_string(), t.to_string()));
            }
            let fails = inventory(&files, &links, &cites, is_docs, &BUDGET);
            for f in &fails {
                println!("FAIL {f}");
            }
            println!("inventory: {} tracked files, {} documentation, {} failures", files.len(), files.iter().filter(|(p, _)| is_docs(p)).count(), fails.len());
            Ok(fails.is_empty())
        }
        ["plan", n, k, times, cases, mutants] => {
            let n: usize = n.parse().map_err(|_| format!("N `{n}` is not a number"))?;
            let k: usize = k.parse().map_err(|_| format!("K `{k}` is not a number"))?;
            if n == 0 || k == 0 || k > n {
                return Err(format!("shard {k} of {n}"));
            }
            let mut t = BTreeMap::new();
            for row in lines(&read(times)?).into_iter().filter(|r| !r.starts_with('#')) {
                let f: Vec<&str> = row.split('\t').collect();
                let s: f64 = f.get(2).and_then(|s| s.parse().ok()).ok_or(format!("{times}: `{row}` is not KIND<tab>ID<tab>SECONDS"))?;
                t.insert((f[0].to_string(), f[1].to_string()), s);
            }
            for (kind, id) in plan(&items(&read(cases)?, &read(mutants)?)?, &t, n, k) {
                println!("{kind} {id}");
            }
            Ok(true)
        }
        ["aggregate", cases, mutants, ran @ ..] if !ran.is_empty() => {
            let expected = items(&read(cases)?, &read(mutants)?)?;
            let ran = ran.iter().map(|r| read(r).and_then(|t| record(&t).map_err(|e| format!("{r}: {e}")))).collect::<Result<Vec<_>, _>>()?;
            let fails = aggregate(&expected, &ran);
            for f in &fails {
                println!("FAIL {f}");
            }
            let total: usize = ran.iter().map(Vec::len).sum();
            println!("aggregate: {} expected ({} cases, {} mutants), {total} ran across {} shards, {} failures", expected.len(),
                expected.iter().filter(|i| i.0 == "case").count(), expected.iter().filter(|i| i.0 == "mutant").count(), ran.len(), fails.len());
            Ok(fails.is_empty())
        }
        _ => Err("usage: tviburi-route classify CHANGED | inventory LS CITATIONS | plan N K TIMES CASES MUTANTS | aggregate CASES MUTANTS RAN...".into()),
    }
}

fn main() -> ExitCode {
    let args: Vec<String> = std::env::args().skip(1).collect();
    match run(&args) {
        Ok(true) => ExitCode::SUCCESS,
        Ok(false) => ExitCode::from(1),
        Err(e) => {
            eprintln!("tviburi-route: {e}");
            ExitCode::from(2)
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn f(p: &str, body: &str) -> (String, Vec<u8>) {
        (p.into(), body.as_bytes().to_vec())
    }
    fn budget_scripts() -> Vec<(String, Vec<u8>)> {
        vec![
            f("tests/1201/test-budget.sh", "step=$here/tests/1301/ci-memory.sh\ncp \"$here/tests/1201/fixtures/P.cs\" x"),
            f("tests/1301/ci-memory.sh", "IMAGE=$(sed x \"$here/tests/spike.sh\")\n-v \"$here/tests/1301:/h:ro\" -v \"$here/tools/tviburi-budget:/src\""),
            f("tests/1301/replay-1301.sh", "/repo/model/X.cs"),
        ]
    }
    fn tree() -> Vec<(String, Vec<u8>)> {
        let mut t = budget_scripts();
        t.extend([f("README.md", "# x"), f("docs/a.md", "a"), f("model/G.cs", "// cites docs/work-orders/a.md"), f("tests/notes.md", "n")]);
        t
    }
    fn cites() -> BTreeSet<(String, String)> {
        [("model/G.cs", "a.md"), ("model/G.cs", "docs/")].iter().map(|(a, b)| (a.to_string(), b.to_string())).collect()
    }

    #[test]
    fn classes() {
        assert!(is_docs("docs/work-orders/TVI-WO-1202.md") && is_docs("README.md") && is_docs("LICENSE-MIT"));
        for p in ["docs/work-orders/TVI-WO-1301.st-compare.tsv", "docs/x.cs", "docs/x.resc", "docs/X.MD", "tests/1301/visual/notes.md",
            "spike/1150/predictions.md", "docs/../model/x.md", "docs//x.md", "\"docs/\\303\\251.md\"", "sub/README.md", "model/x.cs"] {
            assert!(!is_docs(p), "{p}");
        }
        assert_eq!(classify(&["docs/a.md", "README.md"]), (true, false));
        assert_eq!(classify(&["docs/a.md", "docs/b.tsv"]), (false, false), "one unclassified path routes code");
        // TVI-WO-1206 (A): the three documents the release reads route as code, alone or with documentation.
        for p in READ_DOCS {
            assert!(!is_docs(p), "{p}");
            assert_eq!(classify(&[p]), (false, false), "{p} alone runs the suite");
            assert_eq!(classify(&["docs/a.md", p]), (false, false));
        }
        assert!(is_docs("docs/work-orders/TVI-WO-1206.md") && is_docs("docs/work-orders/TVI-WO-1206.FEEDBACK.md"));
        assert_eq!(classify(&[]), (false, false), "an empty diff routes code");
        assert_eq!(classify(&["tests/1301/gpio.sh"]), (false, true));
        assert_eq!(classify(&[".github/workflows/ci.yml"]).1, true);
        assert_eq!(classify(&["tests/spike.sh.bak"]).1, false, "a file is matched whole, a directory by prefix");
    }

    #[test]
    fn inventory_passes_the_good_tree() {
        assert_eq!(inventory(&tree(), &[], &cites(), is_docs, &BUDGET), Vec::<String>::new());
    }

    #[test]
    fn inventory_refuses_code_classed_as_documentation() {
        fn broad(p: &str) -> bool { p.starts_with("docs/") || ROOT_DOCS.contains(&p) }
        for planted in ["docs/board.resc", "docs/Guard.cs"] {
            let mut t = tree();
            t.push(f(planted, "x"));
            let fails = inventory(&t, &[], &cites(), broad, &BUDGET);
            assert!(fails.iter().any(|e| e.starts_with(planted) && e.contains("not Markdown")), "{planted}: {fails:?}");
        }
    }

    #[test]
    fn inventory_refuses_a_test_that_reads_documentation() {
        let mut t = tree();
        t.push(f("tests/1300/rcc.sh", "grep -c x \"$here/docs/a.md\""));
        let fails = inventory(&t, &[], &cites(), is_docs, &BUDGET);
        assert!(fails.iter().any(|e| e.starts_with("tests/1300/rcc.sh names `a.md`")), "{fails:?}");
        assert!(fails.iter().any(|e| e.starts_with("tests/1300/rcc.sh names `docs/`")), "{fails:?}");
    }

    #[test]
    fn inventory_refuses_a_symlink_into_documentation() {
        for (l, t) in [("tests/notes.md", "../docs/a.md"), ("tests/1300/x", "../../docs"), ("docs/b.md", "../model/G.cs"), ("tests/y", "/etc/passwd"), ("tests/z", "../../../x")] {
            let fails = inventory(&tree(), &[(l.into(), t.into())], &cites(), is_docs, &BUDGET);
            assert_eq!(fails.len(), 1, "{l} -> {t}: {fails:?}");
            assert!(fails[0].starts_with(l) && fails[0].contains("symlink"), "{fails:?}");
        }
        assert!(inventory(&tree(), &[("tests/w".into(), "../model/G.cs".into())], &cites(), is_docs, &BUDGET).is_empty());
        assert_eq!(resolve("tests/1300/x", "../../docs/a.md").as_deref(), Some("docs/a.md"));
    }

    #[test]
    fn inventory_refuses_a_stale_citation() {
        let mut c = cites();
        c.insert(("model/Gone.cs".into(), "a.md".into()));
        assert!(inventory(&tree(), &[], &c, is_docs, &BUDGET).iter().any(|e| e.contains("no longer")));
    }

    #[test]
    fn inventory_refuses_a_budget_path_dropped_from_the_list() {
        for dropped in ["tests/1301/", "tests/spike.sh", "tools/tviburi-budget/", "tests/1201/"] {
            let short: Vec<&str> = BUDGET.iter().copied().filter(|b| *b != dropped).collect();
            let fails = inventory(&tree(), &[], &cites(), is_docs, &short);
            assert!(!fails.is_empty() && fails.iter().all(|e| e.contains("budget")), "{dropped}: {fails:?}");
        }
        // A script of the budget's tests outside the list, though everything it reads is inside it.
        let narrow = ["tests/1201/", "tests/1301/ci-memory.sh", "tests/1301", "tests/spike.sh", "tools/tviburi-budget/"];
        let fails = inventory(&tree(), &[], &cites(), is_docs, &narrow);
        assert_eq!(fails, vec!["tests/1301/replay-1301.sh: a script of the budget's tests, outside the budget's list".to_string()]);
    }

    fn its(n_cases: usize, n_mutants: usize) -> (String, String) {
        let c: Vec<String> = (0..n_cases).map(|i| format!("c{i}")).collect();
        let m: Vec<String> = (0..n_mutants).map(|i| format!("M{i}\tmodel/x.cs\told\tnew\tc{i}")).collect();
        (c.join("\n"), format!("# header\n{}", m.join("\n")))
    }

    #[test]
    fn plan_covers_every_item_once_in_suite_order() {
        let (c, m) = its(143, 197);
        let all = items(&c, &m).unwrap();
        let mut t = BTreeMap::new();
        t.insert(("case".to_string(), "c0".to_string()), 28.0);
        t.insert(("mutant".to_string(), "M5".to_string()), 34.0);
        t.insert(("case".to_string(), "c1".to_string()), 7.0);
        for n in [1, 2, 3, 5, 8] {
            let shards: Vec<_> = (1..=n).map(|k| plan(&all, &t, n, k)).collect();
            let mut joined: Vec<_> = shards.concat();
            assert_eq!(joined.len(), all.len());
            joined.sort_by_key(|i| all.iter().position(|j| j == i).unwrap());
            assert_eq!(joined, all, "n = {n}");
            for s in &shards {
                let pos: Vec<usize> = s.iter().map(|i| all.iter().position(|j| j == i).unwrap()).collect();
                assert!(pos.windows(2).all(|w| w[0] < w[1]), "a shard keeps the suite's order");
            }
            assert!(aggregate(&all, &shards).is_empty());
        }
        // An item with no measured time (c2..) weighs its kind's median and is planned like the rest.
        assert!((1..=5).any(|k| plan(&all, &t, 5, k).contains(&("case".into(), "c142".into()))));
    }

    #[test]
    fn items_refuse_a_duplicate() {
        assert!(items("a\nb\na", "").unwrap_err().contains("twice"));
        assert!(items("a", "M1\tx\ty\tz\tt\nM1\tx\ty\tz\tt").unwrap_err().contains("twice"));
        assert!(items("a b", "").is_err());
    }

    #[test]
    fn aggregate_refuses_a_dropped_a_doubled_and_a_foreign_item() {
        let (c, m) = its(10, 6);
        let all = items(&c, &m).unwrap();
        let mut shards: Vec<_> = (1..=3).map(|k| plan(&all, &BTreeMap::new(), 3, k)).collect();
        assert!(aggregate(&all, &shards).is_empty());
        let dropped = shards[1].pop().unwrap();
        let fails = aggregate(&all, &shards);
        assert_eq!(fails, vec![format!("{} {} did not run in any shard", dropped.0, dropped.1)]);
        shards[1].push(dropped.clone());
        shards[2].push(dropped.clone());
        assert_eq!(aggregate(&all, &shards), vec![format!("{} {} ran 2 times", dropped.0, dropped.1)]);
        shards[2].pop();
        shards[0].push(("case".into(), "renamed".into()));
        assert!(aggregate(&all, &shards)[0].contains("not in the tree's lists"));
        // A shard that recorded nothing (it failed before its record) leaves its items unrun.
        shards[0].pop();
        shards[2].clear();
        assert!(!aggregate(&all, &shards).is_empty());
    }

    #[test]
    fn records_are_strict() {
        assert_eq!(record("case modf\nmutant M01-slave\n").unwrap().len(), 2);
        assert!(record("case modf extra").is_err() && record("ok   modf").is_err() && record("RAN modf").is_err());
    }
}
