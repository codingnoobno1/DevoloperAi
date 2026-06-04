import 'dart:io';

Future<void> resetProject() async {
  print("🔄 Resetting project...");

  final backup = Directory('lib_backup');
  final libDir = Directory('lib');

  if (!backup.existsSync()) {
    print("❌ No backup found in lib_backup/");
    return;
  }

  if (libDir.existsSync()) {
    print("🗑️ Removing current lib/...");
    libDir.deleteSync(recursive: true);
  }

  print("⏪ Restoring lib/ from lib_backup/...");
  backup.renameSync('lib');

  print("✅ Project restored to original state");
}
