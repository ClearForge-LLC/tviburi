//! TVI-WO-1150 (ii) scratch reader, not a tool: RUL-11's record grammar, applied to one framing.
//! A record is the message text after `<source>: `, one JSON object with a string "tviburi", from a line whose
//! source is one of the named guarded peripherals, and whose framing is:
//!   console  `[hh:mm:ss.nnnnnnnnn] ` (21 bytes, Aethelgard's log_stamped, renode.rs L2332) then `[LEVEL] `
//!   logfile  `[hh:mm:ss.nnnnnnnnn]` or `[HH:MM:SS.ffff]`, then one or more spaces, then `[LEVEL] `
//! As Aethelgard's clean_lines does, a `\r` at either end of a line is removed first; one inside is kept.
//! JSON is checked with the reference check's own parser (tools/tviburi-check/src/json.rs), unchanged.
//! Build: rustc --edition 2021 -o records spike/1150/reader/records.rs   (no build file, by the WO)
//! Usage: records console|logfile FILE SOURCE...
//! Prints `REC<TAB>source<TAB>level<TAB>record` per record, then `REJECT<TAB>reason<TAB>line` for each line
//! that holds `"tviburi"` but is not a record, then a count line.
#[path = "../../../tools/tviburi-check/src/json.rs"]
#[allow(dead_code)]
mod json;

const LEVELS: [&str; 5] = ["NOISY", "DEBUG", "INFO", "WARNING", "ERROR"];

fn digits(b: &[u8]) -> bool {
    b.iter().all(u8::is_ascii_digit)
}

/// The text after the stamp, or None. `exact` = the console's single space; otherwise any run of spaces.
fn after_stamp(line: &str, console: bool) -> Option<&str> {
    let b = line.as_bytes();
    let virt = b.len() >= 20 && b[0] == b'[' && digits(&b[1..3]) && b[3] == b':' && digits(&b[4..6]) && b[6] == b':'
        && digits(&b[7..9]) && b[9] == b'.' && digits(&b[10..19]) && b[19] == b']';
    let host = !console && b.len() >= 15 && b[0] == b'[' && digits(&b[1..3]) && b[3] == b':' && digits(&b[4..6])
        && b[6] == b':' && digits(&b[7..9]) && b[9] == b'.' && digits(&b[10..14]) && b[14] == b']';
    let rest = if virt { &line[20..] } else if host { &line[15..] } else { return None };
    if console {
        rest.strip_prefix(' ')
    } else {
        let t = rest.trim_start_matches(' ');
        if t.len() == rest.len() { None } else { Some(t) }
    }
}

fn main() {
    let args: Vec<String> = std::env::args().collect();
    if args.len() < 4 || (args[1] != "console" && args[1] != "logfile") {
        eprintln!("usage: records console|logfile FILE SOURCE...");
        std::process::exit(2);
    }
    let console = args[1] == "console";
    let bytes = std::fs::read(&args[2]).expect("read");
    let text = String::from_utf8_lossy(&bytes);
    let sources = &args[3..];
    let (mut recs, mut rejects) = (0, 0);
    for raw in text.split('\n') {
        let line = raw.trim_matches('\r');
        let reject = |why: &str| println!("REJECT\t{why}\t{}", line.chars().take(160).collect::<String>());
        let has = line.contains("\"tviburi\"");
        let Some(rest) = after_stamp(line, console) else {
            if has { reject("no stamp at column 0"); rejects += 1; }
            continue;
        };
        let parsed = rest.strip_prefix('[').and_then(|r| r.split_once("] ")).filter(|(l, _)| LEVELS.contains(l));
        let Some((level, rest)) = parsed else {
            if has { reject("no level after the stamp"); rejects += 1; }
            continue;
        };
        let Some((source, message)) = rest.split_once(": ") else {
            if has { reject("no source"); rejects += 1; }
            continue;
        };
        if !sources.iter().any(|s| s == source) {
            if has { reject(&format!("source {source} is not guarded")); rejects += 1; }
            continue;
        }
        if !message.starts_with('{') {
            if has { reject("guarded source, message is not a record"); rejects += 1; }
            continue;
        }
        match json::parse(message) {
            Ok(json::Value::Object(m)) if matches!(m.get("tviburi"), Some(json::Value::Str(_))) => {
                println!("REC\t{source}\t{level}\t{message}");
                recs += 1;
            }
            _ => { reject("guarded source, malformed record"); rejects += 1; }
        }
    }
    println!("COUNT\trecords={recs}\trejected={rejects}");
}
