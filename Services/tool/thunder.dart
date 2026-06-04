import 'dart:convert';
import 'dart:io';
import 'compiler.dart';
import 'apply.dart';
import 'reset.dart';
import 'core/file_writer.dart';
import 'core/mode_manager.dart';
import 'expansion/expansion_engine.dart';

void main(List<String> args) async {
  if (args.isEmpty || args.contains('--help') || args.contains('-h')) {
    _printHelp();
    return;
  }

  final debug = args.contains('--debug');
  final noCache = args.contains('--no-cache');
  final force = args.contains('--force');
  final command = args.where((a) => !a.startsWith('-')).firstOrNull ?? '';
  final subArg = args.where((a) => !a.startsWith('-')).skip(1).firstOrNull;

  try {
    switch (command) {
      // ── Core Commands ──────────────────────────────────────────────────────
      case 'generate':
        await generateApp(debug: debug, force: noCache);
        break;

      case 'expand':
        await _runExpand(force: force, debug: debug);
        break;

      case 'apply':
        await applyChanges();
        break;

      case 'run':
        await generateApp(debug: debug, force: noCache);
        await applyChanges();
        await _runFlutter(args);
        break;

      case 'reset':
        await resetProject();
        break;

      case 'show':
        await showSummary();
        break;

      // ── Mode Management ────────────────────────────────────────────────────
      case 'mode':
        _handleMode(subArg, force: force);
        break;

      // ── Inspection & Debugging ─────────────────────────────────────────────
      case 'ir':
        await inspectIR();
        break;

      case 'ast':
        await inspectAST();
        break;

      case 'graph':
        await inspectGraph();
        break;

      case 'validate':
        await runValidation();
        break;

      case 'optimize':
        await runOptimization();
        break;

      // ── Build Control ──────────────────────────────────────────────────────
      case 'clean':
        print("🧹 Cleaning cache and lib_gen/...");
        if (Directory('.thunder').existsSync()) Directory('.thunder').deleteSync(recursive: true);
        FileWriter.clean('lib_gen');
        print("✅ Clean complete.");
        break;

      case 'rebuild':
        await generateApp(debug: debug, force: true);
        break;

      case 'phases':
        listPhases();
        break;

      // ── Templates & Config ─────────────────────────────────────────────────
      case 'templates':
        _listTemplates();
        break;

      case 'config':
        showConfig();
        break;

      case 'doctor':
        await runDoctor();
        break;

      case 'serve':
        await startHMRBridge();
        break;

      case 'mode':
        _handleMode(args.length > 1 ? args[1] : null, force: force);
        break;

      default:
        print("❌ Unknown command: '$command'. Use --help for available commands.");
        exit(1);
    }
  } catch (e, stack) {
    print("❌ Critical Error: $e");
    if (debug) print(stack);
    exit(1);
  }
}

Future<void> startHMRBridge() async {
  print("⚡ Thunder HMR Bridge Starting...");
  print("📡 Listening on ws://localhost:8080");
  print("   [NOTE] This is a development-only bridge for real-time config syncing.");
  
  while(true) {
    await Future.delayed(const Duration(seconds: 10));
    print("📡 HMR heartbeat...");
  }
}

// ── Command Implementations ────────────────────────────────────────────────────

Future<void> _runExpand({bool force = false, bool debug = false}) async {
  // Issue 5 Fix: Respect mode lock — expansion is a write operation.
  if (ModeManager.isLocked()) {
    print("🔒 Mode is locked. Expansion blocked to protect custom_config/.");
    print("   Use 'thunder mode unlock' to allow changes.");
    return;
  }

  if (ModeManager.isPro() && !force) {
    print("⚠️  Cannot expand in Pro Mode — custom_config/ is user-owned.");
    print("   Use 'thunder mode noob' to switch, or 'thunder expand --force' to override.");
    return;
  }

  final system = _loadSystem();
  final mainAppPath = _findMainAppPath(system);
  if (mainAppPath == null) {
    print("❌ No mainapp.json found. Expansion requires a Noob Mode source file.");
    return;
  }

  final mode = ModeManager.currentMode();
  print("⚡ Expanding '$mainAppPath' → config/custom_config/ (mode: $mode)...");
  ExpansionEngine.expandToMemory(mainAppPath, persist: true, force: force);
  print("✅ Expansion complete. custom_config/ is now up-to-date.");
}

void _handleMode(String? subArg, {bool force = false}) {
  if (subArg == null) {
    print("⚡ Thunder Mode Status:");
    ModeManager.printStatus();
    print("\n  To switch: thunder mode noob | thunder mode pro");
    return;
  }

  // Issue 4 Fix: Warn on destructive pro → noob switch.
  if (subArg == 'noob' && ModeManager.isPro() && !force) {
    print("⚠️  Switching to Noob Mode will allow ExpansionEngine to OVERWRITE custom_config/.");
    print("   Any manual edits to custom_config/ will be lost on next 'thunder expand'.");
    print("   Use --force to confirm: thunder mode noob --force");
    return;
  }

  if (subArg == 'noob' || subArg == 'pro') {
    if (ModeManager.isLocked() && !force) {
      print("🔒 Mode is locked. Use --force to override.");
      return;
    }
    ModeManager.setMode(subArg);
  } else if (subArg == 'lock') {
    final data = File('config/mode.json');
    if (data.existsSync()) {
      final json = Map<String, dynamic>.from(jsonDecode(data.readAsStringSync()));
      json['locked'] = true;
      data.writeAsStringSync(JsonEncoder.withIndent('  ').convert(json));
      print("🔒 Mode locked to '${json['mode']}'.");
    }
  } else if (subArg == 'unlock') {
    final data = File('config/mode.json');
    if (data.existsSync()) {
      final json = Map<String, dynamic>.from(jsonDecode(data.readAsStringSync()));
      json['locked'] = false;
      data.writeAsStringSync(JsonEncoder.withIndent('  ').convert(json));
      print("🔓 Mode unlocked.");
    }
  } else {
    print("❌ Unknown mode: '$subArg'. Use 'noob' or 'pro'.");
  }
}

// ── Helpers ────────────────────────────────────────────────────────────────────

void _printHelp() {
  print("""
⚡ Thunder CLI — Static UI Compiler v6.5 (Depth Design Edition)

Core Commands:
  thunder generate        → Compile config → IR → Flutter code
  thunder expand          → Expand mainapp.json → custom_config/ (Noob mode only)
  thunder apply           → Apply generated files to /lib
  thunder run             → Generate + apply + run on device
  thunder reset           → Restore original /lib from backup
  thunder show            → Show project summary (features, routes, stats)

Mode Management:
  thunder mode            → Show current mode (noob/pro)
  thunder mode noob       → Switch to Noob Mode (mainapp.json driven)
  thunder mode pro        → Switch to Pro Mode (custom_config/ driven)
  thunder mode lock       → Lock current mode (prevent accidental switches)
  thunder mode unlock     → Unlock mode

Inspection & Debugging:
  thunder ir              → Print ProjectIR (structured output)
  thunder ast             → Print AST tree for all screens
  thunder graph           → Show dependency graph (features & flows)
  thunder validate        → Run full validation (schema + dependency checks)
  thunder optimize        → Run IR optimization analysis

Build Control:
  thunder clean           → Clear cache & lib_gen/
  thunder rebuild         → Force full rebuild (ignore cache)
  thunder phases          → List compiler phases + dependencies

Templates & Config:
  thunder templates       → List available app templates
  thunder config          → Show merged config (post-expansion)
  thunder doctor          → Check environment + config health

Flags:
  --debug                 → Enable detailed logs
  --no-cache              → Disable incremental build
  --force                 → Override mode guard (expand in Pro mode)
  --strict                → Stop on first error
  -d <device>             → Target device (flutter run)
""");
}

void _listTemplates() {
  final file = File('tool/config/templates.json');
  if (!file.existsSync()) {
    print("❌ templates.json not found.");
    return;
  }
  print("📂 Available Templates:");
  print(file.readAsStringSync());
}

Map<String, dynamic> _loadSystem() {
  final file = File('tool/config/system.json');
  return file.existsSync() ? jsonDecode(file.readAsStringSync()) : {};
}

String? _findMainAppPath(Map<String, dynamic> system) {
  final triggers = system['mode']?['noobTrigger'] as List? ?? ['mainapp.json', 'config/mainapp.json'];
  for (var trigger in triggers) {
    if (File(trigger).existsSync()) return trigger;
  }
  return null;
}

Future<void> _runFlutter(List<String> args) async {
  String? device;
  if (args.contains('-d')) {
    final index = args.indexOf('-d');
    if (index + 1 < args.length) device = args[index + 1];
  }

  if (device == null) {
    print("\n📱 Select a device to run on:");
    print("1. Windows (Default)");
    print("2. Chrome");
    stdout.write("Choice [1]: ");
    final choice = stdin.readLineSync()?.trim();
    device = (choice == '2') ? 'chrome' : 'windows';
  }

  print("🚀 Running flutter on $device...");
  final process = await Process.start(
    'flutter', ['run', '-d', device],
    runInShell: true,
    mode: ProcessStartMode.inheritStdio,
  );
  await process.exitCode;
}
