use std::error::Error;

fn main() -> Result<(), Box<dyn Error>> {
    // using bindgen, generate binding code
   bindgen::Builder::default()
        .header("include/quiche.h")
        .generate()?
        .write_to_file("src/quiche.rs")?;
        
    // csbindgen code, generate C# dll import
    csbindgen::Builder::default()
        .input_bindgen_file("src/quiche.rs")
        .method_filter(|x| { x.starts_with("quiche_") } )
        .rust_file_header("extern crate quiche;\nuse super::quiche::*;")
        .rust_method_prefix("_")
        .csharp_entry_point_prefix("_")
        .csharp_dll_name("quiche_bindgen")
        .csharp_namespace("Cloudflare.Quiche")
        .generate_to_file("src/quiche_ffi.rs", "../Cloudflare.QuicheNet/NativeMethods.g.cs")
        .unwrap();

    Ok(())
}