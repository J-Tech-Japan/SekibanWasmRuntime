use std::{
    env, fs, io,
    path::{Path, PathBuf},
};

fn main() {
    if let Err(error) = generate_asset_module() {
        panic!("could not generate embedded template asset module: {error}");
    }
}

fn generate_asset_module() -> io::Result<()> {
    let manifest_dir = PathBuf::from(env::var_os("CARGO_MANIFEST_DIR").expect("manifest dir"));
    let templates_dir = manifest_dir.join("src").join("templates");
    println!("cargo:rerun-if-changed={}", templates_dir.display());

    let mut assets = Vec::new();
    collect_files(&templates_dir, &templates_dir, &mut assets)?;
    assets.sort_by(|left, right| left.0.cmp(&right.0));

    if assets.is_empty() {
        return Err(io::Error::new(
            io::ErrorKind::NotFound,
            format!("no template assets found under {}", templates_dir.display()),
        ));
    }

    let out_dir = PathBuf::from(env::var_os("OUT_DIR").expect("out dir"));
    let generated = out_dir.join("template_assets.rs");
    let mut source = String::from("pub(crate) static ASSETS: &[(&str, &[u8])] = &[\n");
    for (relative, absolute) in assets {
        source.push_str(&format!(
            "    ({:?}, include_bytes!({:?}) as &'static [u8]),\n",
            relative, absolute,
        ));
    }
    source.push_str("];\n");
    fs::write(generated, source)
}

fn collect_files(
    root: &Path,
    directory: &Path,
    assets: &mut Vec<(String, PathBuf)>,
) -> io::Result<()> {
    let mut entries = fs::read_dir(directory)?.collect::<Result<Vec<_>, io::Error>>()?;
    entries.sort_by_key(|entry| entry.file_name());

    for entry in entries {
        let path = entry.path();
        if path.is_dir() {
            collect_files(root, &path, assets)?;
            continue;
        }
        if path.is_file() {
            let relative = path
                .strip_prefix(root)
                .expect("asset is below template root")
                .to_string_lossy()
                .replace('\\', "/");
            assets.push((relative, path));
        }
    }
    Ok(())
}
