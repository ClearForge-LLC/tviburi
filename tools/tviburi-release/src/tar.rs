//! A ustar archive with fixed metadata (mtime 0, uid and gid 0, no owner names, mode 0644), so two
//! builds of the same files are byte-identical whatever the checkout's mtimes, umask or owner.

pub fn archive(files: &[(&str, &[u8])]) -> Vec<u8> {
    let mut out = Vec::new();
    for (name, data) in files {
        assert!(name.len() < 100 && name.is_ascii(), "tar name {name}");
        let mut h = [0u8; 512];
        h[..name.len()].copy_from_slice(name.as_bytes());
        put_octal(&mut h[100..108], 0o644);
        put_octal(&mut h[108..116], 0);
        put_octal(&mut h[116..124], 0);
        put_octal(&mut h[124..136], data.len() as u64);
        put_octal(&mut h[136..148], 0);
        h[148..156].copy_from_slice(b"        ");
        h[156] = b'0';
        h[257..263].copy_from_slice(b"ustar\0");
        h[263..265].copy_from_slice(b"00");
        let sum: u32 = h.iter().map(|&b| b as u32).sum();
        let field = format!("{sum:06o}\0 ");
        h[148..156].copy_from_slice(field.as_bytes());
        out.extend_from_slice(&h);
        out.extend_from_slice(data);
        out.resize(out.len().div_ceil(512) * 512, 0);
    }
    out.resize(out.len() + 1024, 0);
    out
}

fn put_octal(field: &mut [u8], v: u64) {
    let s = format!("{v:0width$o}\0", width = field.len() - 1);
    field.copy_from_slice(s.as_bytes());
}
