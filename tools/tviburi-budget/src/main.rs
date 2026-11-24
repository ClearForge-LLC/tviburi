//! `tviburi-budget RUNS.tsv`: TVI-WO-1201's memory budget, judged (`RUL-14`: a gate's arithmetic is a program;
//! ruled 2026-11-24T01:05Z). `tests/1301/ci-memory.sh` boots ArduPilot on main's one file and on the branch's, in
//! one CI job, and writes one row per boot: the facts it sampled, never a verdict. This program decides whether
//! each boot counts, computes the two medians and their difference, and refuses by name:
//!   failed-boot    a boot that does not count, on either side; it is never dropped from a median unnoticed
//!   too-few-boots  fewer than 5 boots on a side, or no boot that counts
//!   ceiling        a branch boot over 1,572,864 KiB, the consumer's ceiling (Aethelgard `RUL-13` as amended
//!                  2026-11-23)
//!   budget         the branch's median more than 32 MiB over main's (Scotty, 2026-11-23 evening)
//! A boot counts only if a peak was sampled, Renode reached the boot's end exactly once, at least one witness
//! reports the release of the boot's own side and none another, and, on the branch, no GPIO would-refuse.
//! Exit 0: PASS. Exit 1: refused. Exit 2: the table cannot be read, which fails the step too.
use std::process::ExitCode;

const MIN_BOOTS: usize = 5;
const CEILING_KIB: u64 = 1_572_864;
const BUDGET_KIB: i64 = 32 * 1024;
/// The columns the judge reads, by header name; the table may carry others (the peak's breakdown), which it ignores.
const COLUMNS: [&str; 7] = ["side", "run", "peak_kib", "boot_end", "witnesses_own", "witnesses_other", "would_refuse"];

#[derive(Clone, Copy, Debug, PartialEq)]
enum Side {
    Main,
    Branch,
}

impl Side {
    fn name(self) -> &'static str {
        match self {
            Side::Main => "main",
            Side::Branch => "branch",
        }
    }
}

#[derive(Clone, Debug)]
struct Boot {
    side: Side,
    run: u32,
    /// None: no peak was sampled.
    peak_kib: Option<u64>,
    boot_end: u32,
    witnesses_own: u32,
    witnesses_other: u32,
    would_refuse: u32,
}

impl Boot {
    /// Why this boot does not count; empty if it counts.
    fn faults(&self) -> Vec<String> {
        let mut f = Vec::new();
        if self.peak_kib.unwrap_or(0) == 0 {
            f.push("no peak sampled".to_string());
        }
        if self.boot_end != 1 {
            f.push(format!("boot end {}", self.boot_end));
        }
        if self.witnesses_own == 0 {
            f.push("no witness of its release".to_string());
        }
        if self.witnesses_other != 0 {
            f.push(format!("{} witnesses of another release", self.witnesses_other));
        }
        if self.side == Side::Branch && self.would_refuse != 0 {
            f.push(format!("{} GPIO would-refuse", self.would_refuse));
        }
        f
    }
}

fn parse(text: &str) -> Result<Vec<Boot>, String> {
    let mut lines = text.lines().enumerate().filter(|(_, l)| !l.trim().is_empty());
    let (_, header) = lines.next().ok_or("an empty table: no header")?;
    let names: Vec<&str> = header.split('\t').collect();
    let mut at = [0usize; COLUMNS.len()];
    for (i, c) in COLUMNS.iter().enumerate() {
        at[i] = names.iter().position(|n| n == c).ok_or_else(|| format!("the header has no column `{c}`"))?;
    }
    let mut boots = Vec::new();
    for (n, line) in lines {
        let f: Vec<&str> = line.split('\t').collect();
        if f.len() != names.len() {
            return Err(format!("line {}: {} fields, the header has {}", n + 1, f.len(), names.len()));
        }
        let num = |i: usize| -> Result<u32, String> {
            f[at[i]].parse().map_err(|_| format!("line {}: `{}` is `{}`, not a count", n + 1, COLUMNS[i], f[at[i]]))
        };
        let side = match f[at[0]] {
            "main" => Side::Main,
            "branch" => Side::Branch,
            s => return Err(format!("line {}: side `{s}`, neither main nor branch", n + 1)),
        };
        let peak_kib = match f[at[2]] {
            "" => None,
            p => Some(p.parse().map_err(|_| format!("line {}: peak_kib is `{p}`, not KiB", n + 1))?),
        };
        boots.push(Boot {
            side,
            run: num(1)?,
            peak_kib,
            boot_end: num(3)?,
            witnesses_own: num(4)?,
            witnesses_other: num(5)?,
            would_refuse: num(6)?,
        });
    }
    Ok(boots)
}

/// The median of the counted peaks; for an even count, the mean of the middle two, rounded down.
fn median(mut v: Vec<u64>) -> Option<u64> {
    v.sort_unstable();
    let n = v.len();
    match n {
        0 => None,
        _ if n % 2 == 1 => Some(v[n / 2]),
        _ => Some((v[n / 2 - 1] + v[n / 2]) / 2),
    }
}

struct Verdict {
    /// (refusal name, its line), in the order found.
    refusals: Vec<(&'static str, String)>,
    medians: Option<(u64, u64)>,
}

fn judge(boots: &[Boot]) -> Verdict {
    let mut refusals = Vec::new();
    for b in boots {
        let f = b.faults();
        if !f.is_empty() {
            refusals.push(("failed-boot", format!("{} boot {} does not count ({})", b.side.name(), b.run, f.join("; "))));
        }
    }
    let mut med = [None, None];
    for (k, side) in [Side::Main, Side::Branch].into_iter().enumerate() {
        let n = boots.iter().filter(|b| b.side == side).count();
        if n < MIN_BOOTS {
            refusals.push(("too-few-boots", format!("{} has {n} boots, at least {MIN_BOOTS} are needed", side.name())));
        }
        let counted = boots.iter().filter(|b| b.side == side && b.faults().is_empty());
        med[k] = median(counted.filter_map(|b| b.peak_kib).collect());
        if med[k].is_none() {
            refusals.push(("too-few-boots", format!("no boot on {} counts, so it has no median", side.name())));
        }
    }
    for b in boots.iter().filter(|b| b.side == Side::Branch) {
        if let Some(p) = b.peak_kib.filter(|&p| p > CEILING_KIB) {
            refusals.push(("ceiling", format!("branch boot {} peaked at {p} KiB, over {CEILING_KIB} KiB", b.run)));
        }
    }
    let medians = med[0].zip(med[1]);
    if let Some((m, b)) = medians {
        let d = b as i64 - m as i64;
        if d > BUDGET_KIB {
            refusals.push(("budget", format!("the branch's median is {d} KiB over main's, more than {BUDGET_KIB} KiB (32 MiB)")));
        }
    }
    Verdict { refusals, medians }
}

fn report(v: &Verdict) -> Vec<String> {
    let mut out: Vec<String> = v.refusals.iter().map(|(name, line)| format!("REFUSE {name}: {line}")).collect();
    if let Some((m, b)) = v.medians {
        let d = b as i64 - m as i64;
        out.push(format!("MEDIANS main {m} KiB, branch {b} KiB, difference {d} KiB ({:+.1} MiB)", d as f64 / 1024.0));
    }
    if v.refusals.is_empty() {
        out.push(format!(
            "PASS: every boot counts, every branch boot at or under {CEILING_KIB} KiB, the median within 32 MiB of main's"
        ));
    }
    out
}

fn main() -> ExitCode {
    let args: Vec<String> = std::env::args().collect();
    if args.len() != 2 {
        eprintln!("usage: tviburi-budget RUNS.tsv");
        return ExitCode::from(2);
    }
    let boots = match std::fs::read_to_string(&args[1]).map_err(|e| format!("{}: {e}", args[1])).and_then(|t| parse(&t)) {
        Ok(b) => b,
        Err(e) => {
            eprintln!("tviburi-budget: {e}");
            return ExitCode::from(2);
        }
    };
    let v = judge(&boots);
    for line in report(&v) {
        println!("{line}");
    }
    ExitCode::from(if v.refusals.is_empty() { 0 } else { 1 })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn boot(side: Side, run: u32, peak: u64) -> Boot {
        Boot { side, run, peak_kib: Some(peak), boot_end: 1, witnesses_own: 14, witnesses_other: 0, would_refuse: 0 }
    }
    /// `k` boots on a side at `peak` KiB each, numbered from `from`.
    fn rows(side: Side, peak: u64, k: u32, from: u32) -> Vec<Boot> {
        (from..from + k).map(|r| boot(side, r, peak)).collect()
    }
    fn names(boots: &[Boot]) -> Vec<&'static str> {
        let mut n: Vec<_> = judge(boots).refusals.iter().map(|r| r.0).collect();
        n.sort_unstable();
        n.dedup();
        n
    }
    fn both(main: u64, branch: u64) -> Vec<Boot> {
        [rows(Side::Main, main, 5, 1), rows(Side::Branch, branch, 5, 1)].concat()
    }

    // The four the ruling names.
    #[test]
    fn planted_64_mib_is_refused_by_budget_alone() {
        // CI 45294287114's medians: main 784,572 KiB, the planted branch 850,368 KiB.
        let b = both(784_572, 850_368);
        assert_eq!(names(&b), ["budget"]);
        assert_eq!(judge(&b).medians, Some((784_572, 850_368)));
    }
    #[test]
    fn main_against_main_passes_near_zero() {
        // CI 45294287114's gate: every boot, main 784,420 and branch 785,552 at the median.
        let m = [777_620, 781_080, 784_784, 784_676, 784_420];
        let b = [804_196, 785_552, 784_300, 780_768, 803_760];
        let mut boots: Vec<Boot> = m.iter().enumerate().map(|(i, &p)| boot(Side::Main, i as u32 + 1, p)).collect();
        boots.extend(b.iter().enumerate().map(|(i, &p)| boot(Side::Branch, i as u32 + 1, p)));
        let v = judge(&boots);
        assert!(v.refusals.is_empty());
        assert_eq!(v.medians, Some((784_420, 785_552)));
        assert!(report(&v).last().unwrap().starts_with("PASS"));
    }
    #[test]
    fn a_boot_with_no_sample_fails_and_is_not_a_low_peak() {
        let mut b = both(1_000_000, 1_000_000);
        b[9].peak_kib = None;
        assert_eq!(names(&b), ["failed-boot"]);
        let mut b = both(1_000_000, 1_000_000);
        b[9].peak_kib = Some(0);
        assert_eq!(names(&b), ["failed-boot"]);
    }
    #[test]
    fn fewer_than_5_boots_is_refused() {
        let b = [rows(Side::Main, 1_000_000, 4, 1), rows(Side::Branch, 1_000_000, 4, 1)].concat();
        assert_eq!(names(&b), ["too-few-boots"]);
        assert_eq!(names(&rows(Side::Main, 1_000_000, 5, 1)), ["too-few-boots"]);
    }

    // The boundaries.
    #[test]
    fn budget_at_32_mib_passes_and_1_kib_over_refuses() {
        assert!(names(&both(1_000_000, 1_032_768)).is_empty());
        assert_eq!(names(&both(1_000_000, 1_032_769)), ["budget"]);
    }
    #[test]
    fn ceiling_exact_passes_and_1_kib_over_refuses() {
        assert!(names(&both(1_572_864, 1_572_864)).is_empty());
        let mut b = both(1_550_000, 1_550_000);
        b[9].peak_kib = Some(1_572_865);
        assert_eq!(names(&b), ["ceiling"]);
    }
    #[test]
    fn a_main_boot_over_the_ceiling_is_not_the_branchs() {
        let mut b = both(1_550_000, 1_550_000);
        b[0].peak_kib = Some(1_600_000);
        assert!(names(&b).is_empty());
    }

    // The median, not the mean or an extreme.
    #[test]
    fn the_median_decides() {
        let mut b = rows(Side::Main, 1_000_000, 5, 1);
        b.extend(rows(Side::Branch, 1_000_000, 3, 1));
        b.extend(rows(Side::Branch, 1_100_000, 2, 4));
        assert!(names(&b).is_empty(), "two high boots of five do not move the median");
        let mut b = rows(Side::Main, 1_000_000, 5, 1);
        b.extend(rows(Side::Branch, 1_000_000, 2, 1));
        b.extend(rows(Side::Branch, 1_040_000, 3, 3));
        assert_eq!(names(&b), ["budget"], "three high boots of five do");
    }
    #[test]
    fn median_of_an_even_count_is_the_mean_of_the_middle_two() {
        assert_eq!(median(vec![4, 1, 3, 2]), Some(2));
        assert_eq!(median(vec![10, 20, 30, 41]), Some(25));
        assert_eq!(median(vec![7]), Some(7));
        assert_eq!(median(vec![]), None);
    }

    // A boot that does not count: never a low peak in a median.
    #[test]
    fn a_failed_boot_with_a_low_peak_is_refused_and_left_out_of_the_median() {
        let mut b = both(1_000_000, 1_100_000);
        for x in b.iter_mut().filter(|x| x.side == Side::Branch).take(3) {
            x.peak_kib = Some(600_000);
            x.boot_end = 0;
        }
        let v = judge(&b);
        assert_eq!(v.medians, Some((1_000_000, 1_100_000)));
        assert_eq!(names(&b), ["budget", "failed-boot"]);
    }
    #[test]
    fn each_fault_alone_fails_the_boot() {
        let faults: [fn(&mut Boot); 5] = [
            |x| x.boot_end = 0,
            |x| x.boot_end = 2,
            |x| x.witnesses_own = 0,
            |x| x.witnesses_other = 14,
            |x| x.would_refuse = 1,
        ];
        for (i, fault) in faults.iter().enumerate() {
            let mut b = both(1_000_000, 1_000_000);
            fault(&mut b[9]);
            assert_eq!(names(&b), ["failed-boot"], "fault {i}");
        }
    }
    #[test]
    fn a_would_refuse_on_main_does_not_fail_the_boot() {
        let mut b = both(1_000_000, 1_000_000);
        b[0].would_refuse = 3;
        assert!(names(&b).is_empty());
    }
    #[test]
    fn a_failed_boot_on_main_is_refused() {
        let mut b = both(1_000_000, 1_000_000);
        b[2].peak_kib = None;
        assert_eq!(names(&b), ["failed-boot"]);
    }
    #[test]
    fn every_branch_boot_failed() {
        let mut b = both(1_000_000, 1_000_000);
        for x in b.iter_mut().filter(|x| x.side == Side::Branch) {
            x.boot_end = 0;
        }
        assert_eq!(names(&b), ["failed-boot", "too-few-boots"]);
        assert_eq!(judge(&b).medians, None);
    }

    // The handoff: read by header name; anything unreadable is an error, never a pass.
    const HEADER: &str = "side\trun\tpeak_kib\twall_s\tboot_end\twitnesses_own\twitnesses_other\twould_refuse\trss_anon";
    #[test]
    fn parses_by_header_name_and_an_empty_peak_is_none() {
        let t = format!("{HEADER}\nmain\t1\t809404\t13.2\t1\t14\t0\t0\t700000\nbranch\t1\t\tnone\t0\t0\t0\t0\t\n");
        let b = parse(&t).unwrap();
        assert_eq!(b.len(), 2);
        assert_eq!(b[0].peak_kib, Some(809_404));
        assert_eq!(b[0].witnesses_own, 14);
        assert_eq!(b[1].peak_kib, None);
        assert_eq!(b[1].faults(), ["no peak sampled", "boot end 0", "no witness of its release"]);
    }
    #[test]
    fn unreadable_tables_are_errors() {
        for bad in [
            String::new(),
            "side\trun\tpeak_kib\n".to_string(),
            format!("{HEADER}\nmain\t1\t809404\n"),
            format!("{HEADER}\nmain\t1\t809404\t1\t1\t14\t0\t0\t0\textra\n"),
            format!("{HEADER}\nmaster\t1\t809404\t1\t1\t14\t0\t0\t0\n"),
            format!("{HEADER}\nmain\t1\t809 MiB\t1\t1\t14\t0\t0\t0\n"),
            format!("{HEADER}\nmain\t1\t809404\t1\t-1\t14\t0\t0\t0\n"),
        ] {
            assert!(parse(&bad).is_err(), "accepted: {bad:?}");
        }
    }
    #[test]
    fn the_report_names_each_refusal_and_states_the_medians() {
        let r = report(&judge(&both(1_000_000, 1_065_536)));
        assert_eq!(r[0], "REFUSE budget: the branch's median is 65536 KiB over main's, more than 32768 KiB (32 MiB)");
        assert_eq!(r[1], "MEDIANS main 1000000 KiB, branch 1065536 KiB, difference 65536 KiB (+64.0 MiB)");
        assert_eq!(r.len(), 2);
    }
}
