import 'dart:convert';
import 'dart:io';
import 'core/models.dart';
import 'core/config_parser.dart';
import 'core/dependency_graph.dart';
import 'core/file_writer.dart';
import 'core/resolver.dart';
import 'core/validator.dart';
import 'core/ir_builder.dart';
import 'core/ir.dart';
import 'core/ast.dart';
import 'core/ir_optimizer.dart';
import 'core/mode_manager.dart';
import 'core/config_validator.dart';
import 'expansion/expansion_engine.dart';
import 'compiler/phase_registry_impl.dart';
import 'utils/logger.dart';
import 'utils/cache_manager.dart';
import 'utils/integrity_checker.dart';

/// The Thunder Static UI Compiler v6.5 (Depth Design Edition).
Future<void> generateApp({bool debug = false, bool force = false}) async {
  Logger.debugEnabled = debug;
  Logger.info("⚡ Thunder Static UI Compiler v6.5 Started...");
  Logger.info("   Mode: ${ModeManager.currentMode().toUpperCase()}");

  final system = _loadSystem();
  final outputDir = system['paths']?['output'] ?? 'lib_gen';
  final mainAppPath = _findMainAppPath(system);

  final env = {
    "debug": debug,
    "strictMode": system['compiler']?['strictMode'] ?? true,
    "compiler_version": "6.5.0-depth",
  };

  // Issue 2 Fix: Existence guard — custom_config/ must exist before any compilation.
  final customConfigDir = system['paths']?['custom_config'] ?? system['paths']?['expanded'] ?? 'config/custom_config';
  if (!Directory(customConfigDir).existsSync()) {
    throw Exception(
      "❌ config/custom_config/ is missing.\n"
      "   In Noob Mode: run 'thunder expand' first.\n"
      "   In Pro Mode:  create custom_config/ and add your config files."
    );
  }

  // Phase 0a: Mode-Aware Expansion (Enforcement)
  if (ModeManager.isNoob() && mainAppPath != null) {
    // Check if expansion has been run at least once (look for the new v5 Structured IR files)
    final isExpanded = File('$customConfigDir/screens.json').existsSync() && 
                       File('$customConfigDir/widgets.json').existsSync();
                       
    if (!isExpanded) {
      throw Exception(
        "❌ Noob Mode requires explicit expansion.\n"
        "   Run: thunder expand"
      );
    }
    Logger.info("⚡ Noob Mode: Using previously expanded custom_config/");
  } else if (ModeManager.isPro()) {
    Logger.info("⚡ Pro Mode: Using manual custom_config/");
  }

  // Phase 0b: Pre-IR Config Validation (Issue 3 already hooked here — keeping it)
  final validator = ConfigValidator.forSystem();
  final configValid = validator.validate();
  if (!configValid) {
    validator.printReport();
    throw Exception("❌ Config validation failed. Fix errors in custom_config/ before generating.");
  }

  // Cache Check (post-validation, so a cached broken state is never used)
  bool isUpToDate = false;
  if (!force) {
    isUpToDate = await CacheManager.isUpToDate(
      mainInputPath: ModeManager.isNoob() ? mainAppPath : null,
      outputDir: outputDir,
      env: env,
    );
  }

  // Frontend: IR Construction
  // ConfigParser now just reads from custom_config (no in-memory fallback needed)
  final model = _parseAndValidate();
  final graph = _buildGraph(model);
  final ir = IRBuilder.build(model, graph);

  if (!ir.isValid) {
    Logger.error("❌ Cannot generate: IR is invalid.");
    for (var err in ir.errors) print("   - $err");
    _invalidateCache();
    throw Exception("IR validation failed. Fix the errors listed above before proceeding.");
  }

  if (isUpToDate && !force) {
    Logger.success("🚀 Project is up-to-date and IR is valid. Skipping generation.");
    return;
  }

  FileWriter.debugMode = debug;
  Logger.info("🧹 Cleaning $outputDir/...");
  FileWriter.clean(outputDir);

  IROptimizer.optimize(ir);

  final registry = PhaseRegistry.standard(model, graph);
  registry.strictMode = env['strictMode'] as bool;

  ir.buildStatus.timestamp = DateTime.now();
  try {
    await registry.runAll(ir);
    IntegrityChecker.verify(outputDir);
    ir.buildStatus.success = true;
  } catch (e) {
    ir.buildStatus.success = false;
    rethrow;
  } finally {
    if (ir.buildStatus.success) {
      CacheManager.updateCache(
        mainInputPath: ModeManager.isNoob() ? mainAppPath : null,
        env: env,
      );
    }
  }

  Logger.success("Optimized Generation Complete in $outputDir/");
}

void _invalidateCache() {
  final file = File(CacheManager.cachePath);
  if (file.existsSync()) file.deleteSync();
}

Future<void> runValidation() async {
  Logger.info("🧪 Running Validation Phase...");
  final model = _loadCurrentModel();
  final graph = _buildGraph(model);
  final ir = IRBuilder.build(model, graph);
  
  if (!ir.isValid) {
    Logger.error("Validation failed with ${ir.errors.length} errors.");
    for (var err in ir.errors) print("  - $err");
  } else {
    Logger.success("Validation passed! IR is clean.");
  }
}

Future<void> runOptimization() async {
  Logger.info("🚀 Running Optimization Analysis...");
  final model = _loadCurrentModel();
  final graph = _buildGraph(model);
  final ir = IRBuilder.build(model, graph);
  IROptimizer.optimize(ir);
}

Future<void> inspectIR() async {
  final model = _loadCurrentModel();
  final graph = _buildGraph(model);
  final ir = IRBuilder.build(model, graph);

  Logger.info("🏛️ ProjectIR Inspector:");
  print(JsonEncoder.withIndent('  ').convert({
    "metadata": ir.metadata,
    "features": ir.features.keys.toList(),
    "services": ir.services.keys.toList(),
    "theme_valid": ir.theme.isValid,
    "is_valid": ir.isValid,
    "build_status": ir.buildStatus.toJson(),
    "optimization": ir.optimization.toJson(),
    "errors": ir.errors,
  }));
}

Future<void> inspectAST() async {
  final model = _loadCurrentModel();
  final graph = _buildGraph(model);
  final ir = IRBuilder.build(model, graph);

  Logger.info("🌳 AST Inspector:");
  for (var feature in ir.features.values) {
    for (var screen in feature.screens) {
      print("\nScreen: ${screen.name}");
      _printASTNode(screen.root, 0);
    }
  }
}

Future<void> inspectGraph() async {
  final model = _loadCurrentModel();
  final graph = _buildGraph(model);
  Logger.info("🔗 Dependency Graph Inspector:");
  graph.printDebug();
}

void listPhases() {
  Logger.info("📦 Compiler Phases:");
  final registry = PhaseRegistry.standard(AppModel.fromJson({}), DependencyGraph());
  for (var phase in registry.phases) {
    print("  - ${phase.name.padRight(20)} (v${phase.version}) ${phase.dependsOn.isNotEmpty ? 'depends on: ${phase.dependsOn}' : ''}");
  }
}

void showConfig() {
  final mainPath = _findMainAppPath(_loadSystem());
  if (mainPath == null) return;
  final expanded = ExpansionEngine.expandToMemory(mainPath);
  print(JsonEncoder.withIndent('  ').convert(expanded));
}

Future<void> runDoctor() async {
  Logger.info("🩺 Thunder Doctor:");
  print("");
  print("  Environment:");
  _checkItem("Flutter SDK", Process.runSync('flutter', ['--version'], runInShell: true).exitCode == 0);
  _checkItem("System Config", File('tool/config/system.json').existsSync());
  _checkItem("Expansion Rules", File('tool/config/expansion_rules.json').existsSync());
  _checkItem("Mode Contract", File('config/mode.json').existsSync());
  _checkItem("Cache Directory", Directory('.thunder').existsSync());

  print("");
  print("  Mode Status:");
  ModeManager.printStatus();

  print("");
  print("  Config Health (custom_config/):");
  final validator = ConfigValidator.forSystem();
  validator.validate();
  validator.printReport();
}

// Helpers
AppModel _loadCurrentModel() {
  return ConfigParser.parse();
}

void _checkItem(String label, bool ok) {
  print("  ${ok ? '✅' : '❌'} ${label.padRight(20)} ${ok ? 'Healthy' : 'Issues found'}");
}

void _printASTNode(WidgetNode? node, int indent) {
  final prefix = "  " * indent;
  if (node == null) return;
  
  String details = "";
  if (node is TextNode) details = " [text: \"${node.text}\"]";
  if (node is ButtonNode) details = " [text: \"${node.text}\", action: ${node.action}]";
  if (node is ContainerNode) details = " [w: ${node.width ?? 'auto'}, h: ${node.height ?? 'auto'}]";
  if (node is ColumnNode) details = " [children: ${node.children.length}]";
  if (node is RowNode) details = " [children: ${node.children.length}]";
  if (node is ListViewNode) details = " [children: ${node.children.length}]";
  
  print("$prefix└─ ${node.runtimeType}$details");
  
  if (node is ColumnNode) {
    for (var child in node.children) _printASTNode(child, indent + 1);
  } else if (node is RowNode) {
    for (var child in node.children) _printASTNode(child, indent + 1);
  } else if (node is ListViewNode) {
    for (var child in node.children) _printASTNode(child, indent + 1);
  } else if (node is ContainerNode && node.child != null) {
    _printASTNode(node.child, indent + 1);
  } else if (node is PaddingNode) {
    _printASTNode(node.child, indent + 1);
  } else if (node is CenterNode) {
    _printASTNode(node.child, indent + 1);
  } else if (node is ExpandedNode) {
    _printASTNode(node.child, indent + 1);
  } else if (node is SafeAreaNode) {
    _printASTNode(node.child, indent + 1);
  } else if (node is SingleChildScrollViewNode) {
    _printASTNode(node.child, indent + 1);
  } else if (node is ScaffoldNode) {
    if (node.appBar != null) _printASTNode(node.appBar, indent + 1);
    if (node.body != null) _printASTNode(node.body, indent + 1);
    if (node.bottomNavigationBar != null) _printASTNode(node.bottomNavigationBar, indent + 1);
  }
}

AppModel _parseAndValidate() {
  final model = ConfigParser.parse();
  Resolver.resolve(model);
  Validator.validate(model);
  return model;
}

DependencyGraph _buildGraph(AppModel model) {
  final graph = DependencyGraph();
  graph.resolve(model);
  return graph;
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

Future<void> showSummary() async {
  inspectIR();
}
