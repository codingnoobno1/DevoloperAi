import 'dart:io';

Future<void> applyChanges() async {
  print("⚡ Applying generated files...");

  final libDir = Directory('lib');
  final genDir = Directory('lib_gen');

  if (!genDir.existsSync()) {
    print("❌ No generated files found. Run 'thunder generate' first.");
    return;
  }

  // Backup original
  final backup = Directory('lib_backup');
  if (backup.existsSync()) backup.deleteSync(recursive: true);
  
  if (libDir.existsSync()) {
    print("📦 Backing up original lib/ to lib_backup/...");
    libDir.renameSync('lib_backup');
  }

  // Move generated → lib
  print("🚀 Moving lib_gen/ to lib/...");
  genDir.renameSync('lib');

  print("✅ Applied successfully. Original code is safe in lib_backup/");
}
