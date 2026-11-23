//! A strict JSON reader, enough for one record per line: objects, arrays, strings, numbers,
//! true, false and null. It rejects duplicate keys, trailing input and anything malformed.

use std::collections::BTreeMap;

#[derive(Debug, PartialEq)]
pub enum Value {
    Null,
    Bool(bool),
    Number(String),
    Str(String),
    Array(Vec<Value>),
    Object(BTreeMap<String, Value>),
}

pub fn parse(text: &str) -> Result<Value, String> {
    let mut p = Parser { s: text.as_bytes(), i: 0 };
    let v = p.value(0)?;
    p.ws();
    if p.i != p.s.len() {
        return Err(format!("trailing input at byte {}", p.i));
    }
    Ok(v)
}

struct Parser<'a> {
    s: &'a [u8],
    i: usize,
}

const MAX_DEPTH: usize = 16;

impl Parser<'_> {
    fn ws(&mut self) {
        while self.i < self.s.len() && matches!(self.s[self.i], b' ' | b'\t' | b'\n' | b'\r') {
            self.i += 1;
        }
    }

    fn eat(&mut self, c: u8) -> Result<(), String> {
        if self.s.get(self.i) == Some(&c) {
            self.i += 1;
            Ok(())
        } else {
            Err(format!("expected '{}' at byte {}", c as char, self.i))
        }
    }

    fn value(&mut self, depth: usize) -> Result<Value, String> {
        if depth > MAX_DEPTH {
            return Err("nested too deeply".into());
        }
        self.ws();
        match self.s.get(self.i) {
            Some(b'{') => self.object(depth),
            Some(b'[') => self.array(depth),
            Some(b'"') => Ok(Value::Str(self.string()?)),
            Some(b't') => self.literal("true", Value::Bool(true)),
            Some(b'f') => self.literal("false", Value::Bool(false)),
            Some(b'n') => self.literal("null", Value::Null),
            Some(b'-' | b'0'..=b'9') => self.number(),
            _ => Err(format!("unexpected input at byte {}", self.i)),
        }
    }

    fn literal(&mut self, word: &str, v: Value) -> Result<Value, String> {
        if self.s[self.i..].starts_with(word.as_bytes()) {
            self.i += word.len();
            Ok(v)
        } else {
            Err(format!("bad literal at byte {}", self.i))
        }
    }

    fn object(&mut self, depth: usize) -> Result<Value, String> {
        self.eat(b'{')?;
        let mut map = BTreeMap::new();
        self.ws();
        if self.eat(b'}').is_ok() {
            return Ok(Value::Object(map));
        }
        loop {
            self.ws();
            let key = self.string()?;
            self.ws();
            self.eat(b':')?;
            let v = self.value(depth + 1)?;
            if map.insert(key.clone(), v).is_some() {
                return Err(format!("duplicate key {key:?}"));
            }
            self.ws();
            if self.eat(b',').is_ok() {
                continue;
            }
            self.eat(b'}')?;
            return Ok(Value::Object(map));
        }
    }

    fn array(&mut self, depth: usize) -> Result<Value, String> {
        self.eat(b'[')?;
        let mut items = Vec::new();
        self.ws();
        if self.eat(b']').is_ok() {
            return Ok(Value::Array(items));
        }
        loop {
            items.push(self.value(depth + 1)?);
            self.ws();
            if self.eat(b',').is_ok() {
                continue;
            }
            self.eat(b']')?;
            return Ok(Value::Array(items));
        }
    }

    fn string(&mut self) -> Result<String, String> {
        self.eat(b'"')?;
        let mut out = String::new();
        loop {
            let c = *self.s.get(self.i).ok_or("unterminated string")?;
            self.i += 1;
            match c {
                b'"' => return Ok(out),
                b'\\' => {
                    let e = *self.s.get(self.i).ok_or("unterminated escape")?;
                    self.i += 1;
                    match e {
                        b'"' => out.push('"'),
                        b'\\' => out.push('\\'),
                        b'/' => out.push('/'),
                        b'b' => out.push('\u{8}'),
                        b'f' => out.push('\u{c}'),
                        b'n' => out.push('\n'),
                        b'r' => out.push('\r'),
                        b't' => out.push('\t'),
                        b'u' => {
                            let hex = self.s.get(self.i..self.i + 4).ok_or("short \\u escape")?;
                            let hex = std::str::from_utf8(hex).map_err(|_| "bad \\u escape")?;
                            let code = u32::from_str_radix(hex, 16).map_err(|_| "bad \\u escape")?;
                            self.i += 4;
                            out.push(char::from_u32(code).ok_or("\\u escape is not a scalar value")?);
                        }
                        _ => return Err(format!("bad escape at byte {}", self.i - 1)),
                    }
                }
                0..=0x1f => return Err(format!("control character in string at byte {}", self.i - 1)),
                _ => {
                    // Copy one UTF-8 sequence whole; the input is a &str, so it is valid.
                    let start = self.i - 1;
                    while self.i < self.s.len() && (self.s[self.i] & 0xC0) == 0x80 {
                        self.i += 1;
                    }
                    out.push_str(std::str::from_utf8(&self.s[start..self.i]).map_err(|_| "bad UTF-8")?);
                }
            }
        }
    }

    fn number(&mut self) -> Result<Value, String> {
        let start = self.i;
        if self.s.get(self.i) == Some(&b'-') {
            self.i += 1;
        }
        let digits = |p: &mut Self| {
            let d = p.i;
            while p.i < p.s.len() && p.s[p.i].is_ascii_digit() {
                p.i += 1;
            }
            p.i > d
        };
        if self.s.get(self.i) == Some(&b'0') {
            self.i += 1;
        } else if !digits(self) {
            return Err(format!("bad number at byte {start}"));
        }
        if self.s.get(self.i) == Some(&b'.') {
            self.i += 1;
            if !digits(self) {
                return Err(format!("bad number at byte {start}"));
            }
        }
        if matches!(self.s.get(self.i), Some(b'e' | b'E')) {
            self.i += 1;
            if matches!(self.s.get(self.i), Some(b'+' | b'-')) {
                self.i += 1;
            }
            if !digits(self) {
                return Err(format!("bad number at byte {start}"));
            }
        }
        Ok(Value::Number(String::from_utf8_lossy(&self.s[start..self.i]).into_owned()))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn accepts_a_record() {
        let v = parse(r#"{"a":"x\"yé","b":[1,-2.5e3,true,null],"c":{}}"#).unwrap();
        let Value::Object(m) = v else { panic!() };
        assert_eq!(m["a"], Value::Str("x\"y\u{e9}".into()));
    }

    #[test]
    fn rejects_malformed_input() {
        for bad in [
            r#"{"a":1,"a":2}"#,
            r#"{"a":1} x"#,
            r#"{"a":1"#,
            r#"{"a":01}"#,
            r#"{"a":"\q"}"#,
            "{\"a\":\"\u{1}\"}",
            r#"{a:1}"#,
            r#"{"a":1,}"#,
            r#"{"a":1} (2)"#,
            r#"{"a":[[[[[[[[[[[[[[[[[[1]]]]]]]]]]]]]]]]]}"#,
        ] {
            assert!(parse(bad).is_err(), "{bad}");
        }
    }
}
