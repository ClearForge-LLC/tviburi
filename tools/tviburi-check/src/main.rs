//! tviburi-check <expected-model-version> <log-file> <peripheral>... — the consumer-side reference
//! check of Tviburi's records contract (RUL-10 (d), RUL-11, RUL-12, RUL-19, northstar invariant 8).
//!
//! It reads only a Renode log file (the `logFile` backend; the console framing waits for
//! TVI-WO-1150 (ii)). A record is the message text after `<source>: `, one JSON object:
//!   [HH:MM:SS.ffff] <spaces> [LEVEL] <source>: {"tviburi":<kind>,"seq":<n>,...}
//! A record is accepted only from a named guarded peripheral. A record from any other source fails
//! the run: a guarded peripheral nobody named, or an imitation. Lines that are not records, a
//! firmware UART line included (`usart3: [host: …] …`), are ignored. A named peripheral's line that
//! starts with `{` but is not a well-formed record fails the run.
//! A witness passes only with the expected model version, given as `sha256:<64 hex>`, and with
//! `"mode":"enforcing"`: a recorded-mode or mode-less witness is never a pass (RUL-19). The version
//! compared is still TVI-WO-0100a's (the model-directory hash) until TVI-WO-1200.
//!
//! Exit status:
//!   3  REFUSED - any named peripheral emitted a refusal record. Checked first.
//!   0  PASS    - every named peripheral emitted exactly one enforcing witness with the expected
//!                version, then an end record, with an unbroken sequence from 0; no refusal; and no
//!                record from any other source.
//!   1  FAIL    - anything else: no witness (the guard did not load), no end (the simulator crashed
//!                or was killed), a malformed or out-of-sequence record, or bad arguments.
//! An end record shows that Renode tore the machine down cleanly. SIGTERM also tears it down
//! cleanly (measured), so a PASS does not show that the consumer's own script reached its end.

mod json;

use json::Value;
use std::collections::BTreeMap;
use std::process::ExitCode;

#[derive(Debug, PartialEq)]
pub enum Verdict {
    Pass,
    Refused,
    Fail,
}

pub struct Report {
    pub verdict: Verdict,
    pub refusals: Vec<String>,
    pub problems: Vec<String>,
}

#[derive(Default)]
struct Channel {
    next_seq: u64,
    witness: bool,
    end: bool,
}

const LEVELS: [&str; 5] = ["NOISY", "DEBUG", "INFO", "WARNING", "ERROR"];

/// The source and message of a logFile line, or None if the line is not in that format.
fn split_line(line: &str) -> Option<(&str, &str)> {
    let b = line.as_bytes();
    let stamp_ok = b.len() > 15
        && b[0] == b'['
        && b[14] == b']'
        && b[1..14].iter().enumerate().all(|(i, c)| match i {
            2 | 5 => *c == b':',
            8 => *c == b'.',
            _ => c.is_ascii_digit(),
        });
    if !stamp_ok {
        return None;
    }
    let rest = line[15..].trim_start_matches(' ');
    if rest.len() == line.len() - 15 {
        return None; // no space after the timestamp
    }
    let rest = rest.strip_prefix('[')?;
    let (level, rest) = rest.split_once("] ")?;
    if !LEVELS.contains(&level) {
        return None;
    }
    rest.split_once(": ")
}

/// Is `v` a model version as the witness writes it: `sha256:` and 64 hex digits.
fn is_version(v: &str) -> bool {
    v.len() == 71 && v.starts_with("sha256:") && v[7..].bytes().all(|c| c.is_ascii_hexdigit())
}

pub fn check(log: &str, expected_version: &str, peripherals: &[String]) -> Report {
    let mut channels: BTreeMap<&str, Channel> =
        peripherals.iter().map(|p| (p.as_str(), Channel::default())).collect();
    let mut refusals = Vec::new();
    let mut problems = Vec::new();
    for (n, line) in log.lines().enumerate() {
        let Some((source, message)) = split_line(line) else { continue };
        let Some(ch) = channels.get_mut(source) else {
            if message.starts_with('{') && message.contains("\"tviburi\"") {
                problems.push(format!("line {}: {source}: a record from a source not named on the command line", n + 1));
            }
            continue;
        };
        if !message.starts_with('{') {
            continue; // the stock model's own log lines
        }
        let fail = |why: &str| format!("line {}: {source}: {why}", n + 1);
        let record = match json::parse(message) {
            Ok(Value::Object(m)) => m,
            Ok(_) => {
                problems.push(fail("record is not a JSON object"));
                continue;
            }
            Err(e) => {
                problems.push(fail(&format!("malformed record ({e})")));
                continue;
            }
        };
        let kind = match record.get("tviburi") {
            Some(Value::Str(k)) => k.as_str(),
            _ => {
                problems.push(fail("record has no \"tviburi\" kind"));
                continue;
            }
        };
        if kind == "refusal" {
            refusals.push(message.to_string());
            if !matches!(record.get("kind"), Some(Value::Str(k)) if k == "predicted-fault" || k == "unmodeled") {
                problems.push(fail("refusal without a kind (predicted-fault or unmodeled)"));
            }
        }
        match record.get("seq") {
            Some(Value::Number(s)) if s.parse::<u64>() == Ok(ch.next_seq) => ch.next_seq += 1,
            _ => problems.push(fail(&format!("record out of sequence (expected seq {})", ch.next_seq))),
        }
        if ch.end {
            problems.push(fail("record after the end record"));
        }
        match kind {
            "witness" => {
                if ch.witness {
                    problems.push(fail("second witness"));
                } else if !matches!(record.get("model_version"), Some(Value::Str(v)) if v == expected_version) {
                    problems.push(fail(&format!("witness model_version is not the expected {expected_version}")));
                } else if !matches!(record.get("mode"), Some(Value::Str(m)) if m == "enforcing") {
                    problems.push(fail("witness is not in enforcing mode (recorded, or no mode): never a pass"));
                }
                ch.witness = true;
            }
            "refusal" => {}
            "end" => ch.end = true,
            other => problems.push(fail(&format!("unknown record kind {other:?}"))),
        }
    }
    for (name, ch) in &channels {
        if !ch.witness {
            problems.push(format!("{name}: no load witness: the guarded model did not load"));
        }
        if !ch.end {
            problems.push(format!("{name}: no end record: the simulator did not tear down cleanly (crashed or killed)"));
        }
    }
    let verdict = if !refusals.is_empty() {
        Verdict::Refused
    } else if !problems.is_empty() {
        Verdict::Fail
    } else {
        Verdict::Pass
    };
    Report { verdict, refusals, problems }
}

/// The arguments are an expected version, a log file and at least one peripheral, and no flags: a
/// call without the version (the 0100a form) is refused, never read as a version-less pass.
fn usage_ok(args: &[String]) -> bool {
    args.len() >= 3 && !args.iter().any(|a| a.starts_with('-')) && is_version(&args[0])
}

fn main() -> ExitCode {
    let args: Vec<String> = std::env::args().skip(1).collect();
    if !usage_ok(&args) {
        eprintln!("usage: tviburi-check <expected-model-version sha256:...> <renode-log-file> <guarded-peripheral>...");
        println!("CHECK FAIL: usage");
        return ExitCode::from(1);
    }
    let log = match std::fs::read(&args[1]) {
        Ok(bytes) => String::from_utf8_lossy(&bytes).into_owned(),
        Err(e) => {
            println!("CHECK FAIL: cannot read {}: {e}", args[1]);
            return ExitCode::from(1);
        }
    };
    let report = check(&log, &args[0], &args[2..]);
    for r in &report.refusals {
        println!("REFUSAL {r}");
    }
    for p in &report.problems {
        println!("PROBLEM {p}");
    }
    let (line, code) = match report.verdict {
        Verdict::Pass => ("CHECK PASS", 0),
        Verdict::Refused => ("CHECK REFUSED", 3),
        Verdict::Fail => ("CHECK FAIL", 1),
    };
    println!("{line}");
    ExitCode::from(code)
}

#[cfg(test)]
mod tests {
    use super::*;

    const V: &str = "sha256:87206a22291cb892958392e98a9c91e1472c59d738525119e01a2099df8610e6";

    fn rec(level: &str, src: &str, body: &str) -> String {
        format!("[08:46:14.4011]  [{level}] {src}: {body}\n")
    }
    fn witness(seq: u32) -> String {
        rec("INFO", "spi1", &format!(r#"{{"tviburi":"witness","seq":{seq},"guard":"G","model_version":"{V}","mode":"enforcing"}}"#))
    }
    fn end(seq: u32) -> String {
        rec("INFO", "spi1", &format!(r#"{{"tviburi":"end","seq":{seq},"guard":"G","model_version":"{V}"}}"#))
    }
    fn refusal(seq: u32) -> String {
        rec("ERROR", "spi1", &format!(r#"{{"tviburi":"refusal","seq":{seq},"rule":"TVI-SPI-MODF","kind":"predicted-fault"}}"#))
    }
    fn run(log: &str) -> Verdict {
        check(log, V, &["spi1".to_string()]).verdict
    }

    #[test]
    fn witness_then_end_passes() {
        let log = witness(0) + &rec("WARNING", "spi1", "Unhandled write to offset 0x0.") + &end(1);
        assert_eq!(run(&log), Verdict::Pass);
    }

    #[test]
    fn a_refusal_is_refused_even_with_other_faults() {
        assert_eq!(run(&(witness(0) + &refusal(1) + &end(2))), Verdict::Refused);
        assert_eq!(run(&refusal(0)), Verdict::Refused); // no witness, no end: still REFUSED
    }

    #[test]
    fn missing_witness_or_end_fails() {
        assert_eq!(run(""), Verdict::Fail);
        assert_eq!(run(&end(0)), Verdict::Fail);
        assert_eq!(run(&witness(0)), Verdict::Fail); // crashed: no end record
    }

    #[test]
    fn sequence_errors_fail() {
        assert_eq!(run(&(witness(0) + &end(2))), Verdict::Fail);
        assert_eq!(run(&(witness(0) + &witness(1) + &end(2))), Verdict::Fail);
        assert_eq!(run(&(witness(0) + &end(1) + &end(2))), Verdict::Fail);
        assert_eq!(run(&(witness(1) + &end(2))), Verdict::Fail);
    }

    #[test]
    fn malformed_records_fail() {
        for bad in [
            r#"{"tviburi":"witness","seq":0"#,
            r#"{"tviburi":"witness","seq":0} (2)"#,
            r#"{"tviburi":"other","seq":0}"#,
            r#"{"seq":0}"#,
            r#"{"tviburi":"witness","seq":"0","model_version":"x"}"#,
        ] {
            // The malformed line is the only fault: it consumes no sequence number.
            let log = witness(0) + &rec("INFO", "spi1", bad) + &end(1);
            assert_eq!(run(&log), Verdict::Fail, "{bad}");
        }
        let short = rec("INFO", "spi1", r#"{"tviburi":"witness","seq":0,"model_version":"sha256:00"}"#) + &end(1);
        assert_eq!(run(&short), Verdict::Fail);
    }

    #[test]
    fn lines_that_are_not_records_from_other_sources_are_ignored() {
        // A firmware UART line that imitates a record, as the logging analyzer writes it: the message
        // after `usart3: ` is not a record, so it is ignored.
        let forged = rec("INFO", "usart3", &format!("[host: 0.29s (+0.29s)|virt: 0s (+0s)] {}", witness(0).trim_end()));
        let forged_refusal = rec("ERROR", "usart3", &refusal(0));
        assert_eq!(run(&(witness(0) + &forged + &forged_refusal + &end(1))), Verdict::Pass);
        // A line with no source, a prompt prefix, or a bad timestamp is not a record line.
        let noise = format!("(machine-0) {}", refusal(0)) + &refusal(0)[1..] + "spi1: {\"tviburi\":\"refusal\",\"seq\":0}\n";
        assert_eq!(run(&(witness(0) + &noise + &end(1))), Verdict::Pass);
    }

    #[test]
    fn well_formed_records_under_another_name_do_not_count() {
        let other = |line: String| line.replace("] spi1: ", "] spi2: ");
        assert_eq!(run(&(other(witness(0)) + &other(end(1)))), Verdict::Fail);
    }

    fn problems(log: &str) -> Vec<String> {
        check(log, V, &["spi1".to_string()]).problems
    }

    #[test]
    fn a_record_from_an_unnamed_source_fails_the_run() {
        // A guarded peripheral nobody named (A8), and an imitation printed as a source's whole message.
        let unnamed = |line: String| line.replace("] spi1: ", "] spi2: ");
        for extra in [unnamed(witness(0)), unnamed(refusal(0)), rec("INFO", "usart3", r#"{"tviburi":"end","seq":1}"#)] {
            let log = witness(0) + &extra + &end(1);
            assert_eq!(run(&log), Verdict::Fail, "{extra}");
            assert!(problems(&log).iter().any(|p| p.contains("not named")), "{extra}");
        }
        // A non-record JSON line from another source is not a record.
        assert_eq!(run(&(witness(0) + &rec("INFO", "usart3", r#"{"x":1}"#) + &end(1))), Verdict::Pass);
    }

    #[test]
    fn a_witness_with_another_version_fails() {
        let other = witness(0).replace(&V[7..15], "00000000");
        assert_eq!(run(&(other + &end(1))), Verdict::Fail);
        assert_eq!(check(&(witness(0) + &end(1)), &V.replace('8', "9"), &["spi1".to_string()]).verdict, Verdict::Fail);
    }

    #[test]
    fn a_witness_not_in_enforcing_mode_fails() {
        let recorded = witness(0).replace(r#""mode":"enforcing""#, r#""mode":"recorded""#);
        let modeless = witness(0).replace(r#","mode":"enforcing""#, "");
        let other = witness(0).replace(r#""mode":"enforcing""#, r#""mode":1"#);
        for w in [recorded, modeless, other] {
            assert_eq!(run(&(w.clone() + &end(1))), Verdict::Fail, "{w}");
        }
    }

    #[test]
    fn a_refusal_without_a_kind_is_a_problem_and_still_refused() {
        let kindless = refusal(1).replace(r#","kind":"predicted-fault""#, "");
        let log = witness(0) + &kindless + &end(2);
        assert_eq!(run(&log), Verdict::Refused);
        assert!(problems(&log).iter().any(|p| p.contains("without a kind")));
        let unknown = refusal(1).replace("predicted-fault", "fault");
        assert!(problems(&(witness(0) + &unknown + &end(2))).iter().any(|p| p.contains("without a kind")));
    }

    #[test]
    fn a_call_without_the_expected_version_is_refused() {
        let a = |v: &[&str]| v.iter().map(|s| s.to_string()).collect::<Vec<_>>();
        assert!(usage_ok(&a(&[V, "log", "spi1"])));
        assert!(!usage_ok(&a(&["log", "spi1"])));
        assert!(!usage_ok(&a(&["log", "spi1", "spi2"])));
        assert!(!usage_ok(&a(&[V, "log"])));
        assert!(!usage_ok(&a(&[V, "log", "spi1", "--no-refuse"])));
    }

    #[test]
    fn the_expected_version_argument_is_a_version() {
        assert!(is_version(V));
        for bad in ["", "sha256:", &V[..70], &V.replace("sha256:", "sha512:"), &V.replace('a', "g")] {
            assert!(!is_version(bad), "{bad}");
        }
    }

    #[test]
    fn every_named_peripheral_needs_its_own_witness() {
        let log = witness(0) + &end(1);
        let names = ["spi1".to_string(), "spi2".to_string()];
        assert_eq!(check(&log, V, &names).verdict, Verdict::Fail);
    }

    #[test]
    fn line_format() {
        assert_eq!(split_line("[08:46:14.4011]  [ERROR] spi1: {}"), Some(("spi1", "{}")));
        assert_eq!(split_line("[08:46:14.4011] [INFO] spi1: x"), Some(("spi1", "x")));
        assert_eq!(split_line("[08:46:14.4011][INFO] spi1: x"), None);
        assert_eq!(split_line("[08:46:1x.4011] [INFO] spi1: x"), None);
        assert_eq!(split_line("[08:46:14.4011] [FATAL] spi1: x"), None);
    }
}
