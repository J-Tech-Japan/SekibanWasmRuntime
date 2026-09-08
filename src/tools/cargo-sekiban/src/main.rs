#![deny(unsafe_code)]

use std::{
    env,
    fs::{self, File},
    io::Write,
    path::{Component, Path, PathBuf},
    process,
};

#[cfg(unix)]
use std::os::unix::fs::PermissionsExt;

mod assets {
    include!(concat!(env!("OUT_DIR"), "/template_assets.rs"));
}

const HELP: &str = r#"cargo-sekiban - scaffold a standalone Sekiban Rust WASM Decider project

Usage:
  cargo sekiban new <name> --template decider --mode <registry|dev> [options]

Commands:
  new       Generate a standalone Rust Decider project from an embedded template.

Options for `new`:
  --template, -t <name>  Template family. The supported value is `decider`.
  --mode, -m <mode>      Dependency mode: `registry` or self-contained `dev`.
  --path, --dir, -d <p>  Exact output directory. Defaults to ./<name>.
  --force                Allow generation into a non-empty directory.
  --help, -h             Show this help text.
  --version, -V          Show the package version.

Examples:
  cargo sekiban new weather-app --template decider --mode registry
  cargo sekiban new weather-app --template decider --mode dev --path ./scratch/weather

The generated project contains no downloaded templates or runtime code. The
registry mode pins the published Sekiban Rust crates to =0.1.0; dev mode
vendors the required source crates inside the generated workspace.
"#;

const NEW_HELP: &str = r#"cargo sekiban new - generate a standalone Rust Decider project

Usage:
  cargo sekiban new <name> --template decider --mode <registry|dev> [options]

The output directory defaults to ./<name>. Use --force only when replacing an
existing non-empty directory is intentional.
"#;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Mode {
    Registry,
    Dev,
}

impl Mode {
    fn parse(value: &str) -> Result<Self, String> {
        match value {
            "registry" => Ok(Self::Registry),
            "dev" => Ok(Self::Dev),
            _ => Err(format!(
                "unknown --mode value `{value}` (expected registry or dev)"
            )),
        }
    }

    fn as_str(self) -> &'static str {
        match self {
            Self::Registry => "registry",
            Self::Dev => "dev",
        }
    }
}

#[derive(Debug, PartialEq, Eq)]
struct NewOptions {
    name: String,
    template: String,
    mode: Mode,
    output: PathBuf,
    force: bool,
}

#[derive(Debug)]
struct ProjectNames {
    display: String,
    kebab: String,
    snake: String,
    pascal: String,
}

fn main() {
    if let Err(error) = run(env::args().skip(1).collect()) {
        eprintln!("error: {error}");
        process::exit(1);
    }
}

fn run(mut args: Vec<String>) -> Result<(), String> {
    if args.first().is_some_and(|value| value == "sekiban") {
        args.remove(0);
    }

    match args.first().map(String::as_str) {
        None | Some("help") | Some("--help") | Some("-h") => {
            println!("{HELP}");
            Ok(())
        }
        Some("--version") | Some("-V") | Some("version") => {
            println!("cargo-sekiban {}", env!("CARGO_PKG_VERSION"));
            Ok(())
        }
        Some("new") => {
            if args.iter().any(|value| value == "--help" || value == "-h") {
                println!("{NEW_HELP}");
                return Ok(());
            }
            let options = parse_new(&args[1..])?;
            generate_project(&options)
        }
        Some(command) => Err(format!(
            "unknown command `{command}` (run `cargo sekiban --help` for usage)"
        )),
    }
}

fn parse_new(args: &[String]) -> Result<NewOptions, String> {
    let mut name = None;
    let mut template = None;
    let mut mode = None;
    let mut output = None;
    let mut force = false;
    let mut index = 0;

    while index < args.len() {
        let argument = &args[index];
        match argument.as_str() {
            "--template" | "-t" => {
                template = Some(next_value(args, &mut index, argument)?);
            }
            "--mode" | "-m" => {
                mode = Some(Mode::parse(&next_value(args, &mut index, argument)?)?);
            }
            "--path" | "--dir" | "-d" => {
                output = Some(PathBuf::from(next_value(args, &mut index, argument)?));
            }
            "--force" => force = true,
            value if value.starts_with('-') => {
                return Err(format!(
                    "unknown option `{value}` (run `cargo sekiban new --help`)"
                ));
            }
            value => {
                if name.replace(value.to_string()).is_some() {
                    return Err("new accepts exactly one project name".to_string());
                }
            }
        }
        index += 1;
    }

    let name = name.ok_or_else(|| "project name is required".to_string())?;
    let template = template.ok_or_else(|| "--template decider is required".to_string())?;
    if template != "decider" {
        return Err(format!(
            "unknown --template value `{template}` (only decider is supported)"
        ));
    }
    let mode = mode.ok_or_else(|| "--mode registry or --mode dev is required".to_string())?;
    validate_project_name(&name)?;

    Ok(NewOptions {
        output: output.unwrap_or_else(|| PathBuf::from(&name)),
        name,
        template,
        mode,
        force,
    })
}

fn next_value(args: &[String], index: &mut usize, option: &str) -> Result<String, String> {
    *index += 1;
    args.get(*index)
        .filter(|value| !value.starts_with('-'))
        .cloned()
        .ok_or_else(|| format!("{option} requires a value"))
}

fn validate_project_name(name: &str) -> Result<(), String> {
    if name.is_empty()
        || !name.as_bytes()[0].is_ascii_alphabetic()
        || name.bytes().any(|character| {
            !(character.is_ascii_alphanumeric() || character == b'-' || character == b'_')
        })
        || name.ends_with('-')
        || name.ends_with('_')
        || name.contains("--")
        || name.contains("__")
    {
        return Err(format!(
            "invalid project name `{name}` (use letters, numbers, `-`, or `_`, starting with a letter)"
        ));
    }
    Ok(())
}

fn project_names(name: &str) -> ProjectNames {
    let words = name
        .split(['-', '_'])
        .map(|word| word.to_ascii_lowercase())
        .collect::<Vec<_>>();
    let kebab = words.join("-");
    let snake = words.join("_");
    let pascal = words
        .iter()
        .map(|word| {
            let mut characters = word.chars();
            characters
                .next()
                .map(|first| first.to_ascii_uppercase().to_string() + characters.as_str())
                .unwrap_or_default()
        })
        .collect::<String>();

    ProjectNames {
        display: name.to_string(),
        kebab,
        snake,
        pascal,
    }
}

fn generate_project(options: &NewOptions) -> Result<(), String> {
    let names = project_names(&options.name);
    let output = &options.output;
    debug_assert_eq!(options.template, "decider");
    ensure_output_directory(output, options.force)?;

    let prefix = match options.mode {
        Mode::Registry => "registry/",
        Mode::Dev => "dev/",
    };
    let selected_assets = assets::ASSETS
        .iter()
        .filter(|(path, _)| path.starts_with(prefix))
        .collect::<Vec<_>>();
    if selected_assets.is_empty() {
        return Err(format!(
            "embedded {} template assets are missing; run the deterministic template sync",
            options.mode.as_str()
        ));
    }

    for (asset_path, bytes) in selected_assets {
        let template_relative = asset_path
            .strip_prefix(prefix)
            .expect("asset has selected mode prefix");
        ensure_safe_relative_path(template_relative)?;
        let relative = render_output_path(template_relative, &names);
        ensure_safe_relative_path(&relative)?;
        let destination = output.join(&relative);
        if let Some(parent) = destination.parent() {
            fs::create_dir_all(parent)
                .map_err(|error| format!("create {}: {error}", parent.display()))?;
        }

        let rendered = render_asset(bytes, &names);
        let mut file = File::create(&destination)
            .map_err(|error| format!("create {}: {error}", destination.display()))?;
        file.write_all(&rendered)
            .map_err(|error| format!("write {}: {error}", destination.display()))?;
        set_generated_permissions(&destination)?;
    }

    let output_display = fs::canonicalize(output)
        .unwrap_or_else(|_| output.to_path_buf())
        .display()
        .to_string();
    println!(
        "generated `{}` ({}) at {}",
        options.name,
        options.mode.as_str(),
        output_display
    );
    Ok(())
}

fn ensure_output_directory(output: &Path, force: bool) -> Result<(), String> {
    if output.exists() {
        if !output.is_dir() {
            return Err(format!(
                "output path is not a directory: {}",
                output.display()
            ));
        }
        let mut entries = fs::read_dir(output)
            .map_err(|error| format!("read output directory {}: {error}", output.display()))?;
        if entries.next().is_some() && !force {
            return Err(format!(
                "refusing to generate into non-empty directory: {} (use --force)",
                output.display()
            ));
        }
    } else {
        fs::create_dir_all(output)
            .map_err(|error| format!("create output directory {}: {error}", output.display()))?;
    }
    Ok(())
}

fn ensure_safe_relative_path(path: &str) -> Result<(), String> {
    let candidate = Path::new(path);
    if candidate.is_absolute()
        || candidate.components().any(|component| {
            matches!(
                component,
                Component::ParentDir | Component::RootDir | Component::Prefix(_)
            )
        })
    {
        return Err(format!("embedded template path is unsafe: {path}"));
    }
    Ok(())
}

fn render_asset(bytes: &[u8], names: &ProjectNames) -> Vec<u8> {
    let Ok(text) = std::str::from_utf8(bytes) else {
        return bytes.to_vec();
    };
    render_text(text, names).into_bytes()
}

fn render_output_path(path: &str, names: &ProjectNames) -> String {
    let rendered = render_text(path, names);
    let Some((parent, filename)) = rendered.rsplit_once('/') else {
        return restore_packaging_filename(&rendered).to_string();
    };
    format!("{parent}/{}", restore_packaging_filename(filename))
}

fn restore_packaging_filename(filename: &str) -> &str {
    match filename {
        "Cargo.toml.template" => "Cargo.toml",
        "gitignore.template" => ".gitignore",
        other => other,
    }
}

fn render_text(text: &str, names: &ProjectNames) -> String {
    let replacements = [
        ("__SEKIBAN_PROJECT_NAME__", names.display.as_str()),
        ("__SEKIBAN_PROJECT_KEBAB__", names.kebab.as_str()),
        ("__SEKIBAN_PROJECT_SNAKE__", names.snake.as_str()),
        ("__SEKIBAN_PROJECT_PASCAL__", names.pascal.as_str()),
    ];
    let rendered = replacements
        .iter()
        .fold(text.to_string(), |result, (token, value)| {
            result.replace(token, value)
        });
    rendered
}

fn set_generated_permissions(path: &Path) -> Result<(), String> {
    #[cfg(unix)]
    if path.extension().is_some_and(|extension| extension == "sh") {
        let mut permissions = fs::metadata(path)
            .map_err(|error| format!("stat {}: {error}", path.display()))?
            .permissions();
        permissions.set_mode(0o755);
        fs::set_permissions(path, permissions)
            .map_err(|error| format!("set permissions {}: {error}", path.display()))?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn arguments(values: &[&str]) -> Vec<String> {
        values.iter().map(|value| (*value).to_string()).collect()
    }

    #[test]
    fn parses_the_approved_new_contract() {
        let parsed = parse_new(&arguments(&[
            "weather-app",
            "--template",
            "decider",
            "--mode",
            "dev",
            "--path",
            "scratch/weather-app",
            "--force",
        ]))
        .expect("approved invocation parses");

        assert_eq!(
            parsed,
            NewOptions {
                name: "weather-app".to_string(),
                template: "decider".to_string(),
                mode: Mode::Dev,
                output: PathBuf::from("scratch/weather-app"),
                force: true,
            }
        );
    }

    #[test]
    fn rejects_missing_template_and_mode() {
        let missing_template = parse_new(&arguments(&["weather-app", "--mode", "registry"]))
            .expect_err("template is required");
        assert!(missing_template.contains("--template decider is required"));

        let missing_mode = parse_new(&arguments(&["weather-app", "--template", "decider"]))
            .expect_err("mode is required");
        assert!(missing_mode.contains("--mode registry or --mode dev is required"));
    }

    #[test]
    fn rejects_unknown_template_mode_and_unsafe_names() {
        let template = parse_new(&arguments(&[
            "weather-app",
            "--template",
            "other",
            "--mode",
            "registry",
        ]))
        .expect_err("unknown template is rejected");
        assert!(template.contains("only decider is supported"));

        let mode = parse_new(&arguments(&[
            "weather-app",
            "--template",
            "decider",
            "--mode",
            "local",
        ]))
        .expect_err("unknown mode is rejected");
        assert!(mode.contains("expected registry or dev"));

        validate_project_name("../weather").expect_err("path traversal is rejected");
        validate_project_name("weather--app").expect_err("empty name component is rejected");
    }

    #[test]
    fn renders_all_project_name_forms_without_leaking_tokens() {
        let names = project_names("Weather-App");
        let rendered = String::from_utf8(render_asset(
            b"__SEKIBAN_PROJECT_NAME__ __SEKIBAN_PROJECT_KEBAB__ __SEKIBAN_PROJECT_SNAKE__ __SEKIBAN_PROJECT_PASCAL__",
            &names,
        ))
        .expect("rendered text is utf8");
        assert_eq!(rendered, "Weather-App weather-app weather_app WeatherApp");
        assert!(!rendered.contains("__SEKIBAN_"));
    }

    #[test]
    fn restores_packaging_safe_template_filenames() {
        let names = project_names("weather-app");
        assert_eq!(
            render_output_path("registry/Cargo.toml.template", &names),
            "registry/Cargo.toml"
        );
        assert_eq!(
            render_output_path("registry/gitignore.template", &names),
            "registry/.gitignore"
        );
        assert_eq!(
            render_output_path(
                "registry/AppHost/__SEKIBAN_PROJECT_PASCAL__.AppHost.csproj",
                &names
            ),
            "registry/AppHost/WeatherApp.AppHost.csproj"
        );
    }

    #[test]
    fn embedded_inventory_is_deterministic_and_contains_both_modes() {
        assert!(assets::ASSETS.len() > 10);
        assert!(assets::ASSETS
            .iter()
            .any(|(path, _)| path.starts_with("registry/")));
        assert!(assets::ASSETS
            .iter()
            .any(|(path, _)| path.starts_with("dev/")));
        assert!(assets::ASSETS
            .iter()
            .any(|(path, _)| *path == "registry/scripts/verify-no-local-sekiban-paths.sh"));
        assert!(assets::ASSETS
            .iter()
            .any(|(path, _)| *path == "dev/scripts/verify-vendor.sh"));
        for (left, (path, _)) in assets::ASSETS.iter().enumerate() {
            assert!(
                ensure_safe_relative_path(path).is_ok(),
                "unsafe asset {path}"
            );
            assert!(
                assets::ASSETS[left + 1..]
                    .iter()
                    .all(|(other, _)| other != path),
                "duplicate embedded asset {path}"
            );
        }
    }
}
