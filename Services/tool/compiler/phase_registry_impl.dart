import '../core/models.dart';
import '../core/dependency_graph.dart';
import '../core/ir.dart';
import '../generators/screen_generator.dart';
import '../generators/main_generator.dart';
import '../generators/theme_generator.dart';
import '../generators/api_generator.dart';
import '../generators/db_generator.dart';
import '../generators/route_generator.dart';
import '../generators/state_generator.dart';
import '../generators/repository_generator.dart';
import '../generators/navigation_service_generator.dart';
import '../generators/ui_generator.dart';
import '../generators/summary_generator.dart';
import '../utils/logger.dart';
import 'phase_registry.dart';

class PhaseRegistry {
  final List<GeneratorPhase> phases;
  bool strictMode = true;

  PhaseRegistry(this.phases);

  static PhaseRegistry standard(AppModel model, DependencyGraph graph) {
    final phases = [
      _AdapterPhase('Theme', ThemeGenerator(), model, graph),
      _AdapterPhase('API', ApiGenerator(), model, graph),
      _AdapterPhase('Database', DbGenerator(), model, graph),
      _AdapterPhase('Navigation Service', NavigationServiceGenerator(), model, graph),
      _AdapterPhase('UI Components', UIGenerator(), model, graph, dependsOn: ['Theme']),
      _AdapterPhase('Repositories', RepositoryGenerator(), model, graph, dependsOn: ['API', 'Database']),
      _AdapterPhase('State (Bloc)', StateGenerator(), model, graph, dependsOn: ['Repositories']),
      _AdapterPhase('Screens', ScreenGenerator(), model, graph, dependsOn: ['State (Bloc)', 'UI Components']),
      _AdapterPhase('Routes', RouteGenerator(), model, graph, dependsOn: ['Screens']),
      _AdapterPhase('Main Entry', MainGenerator(), model, graph, dependsOn: ['Routes']),
      _AdapterPhase('Summary', SummaryGenerator(), model, graph, 
        dependsOn: ['Main Entry'], 
        isCritical: false,
        isIRNative: true, // Marker for native IR support
      ),
    ];

    return PhaseRegistry(_topologicalSort(phases));
  }

  Future<void> runAll(ProjectIR ir) async {
    for (final phase in phases) {
      Logger.phase("${phase.name} (v${phase.version})");
      try {
        await phase.run(ir);
        Logger.success("${phase.name} completed.");
      } catch (e, stack) {
        if (phase.isCritical) {
          Logger.error("CRITICAL PHASE FAILED: ${phase.name}", e);
          if (Logger.debugEnabled) print(stack);
          if (strictMode) rethrow;
        } else {
          Logger.warn("OPTIONAL PHASE FAILED: ${phase.name}. Continuing...");
          if (Logger.debugEnabled) Logger.debug(e.toString());
        }
      }
    }
  }

  static List<GeneratorPhase> _topologicalSort(List<GeneratorPhase> phases) {
    final sorted = <GeneratorPhase>[];
    final visited = <String>{};
    final path = <String>{};

    void visit(GeneratorPhase phase) {
      if (visited.contains(phase.name)) return;
      if (path.contains(phase.name)) {
        throw Exception("Circular dependency detected in compiler phases: ${path.join(' -> ')} -> ${phase.name}");
      }

      path.add(phase.name);
      for (final depName in phase.dependsOn) {
        final depPhase = phases.firstWhere(
          (p) => p.name == depName,
          orElse: () => throw Exception("Phase '${phase.name}' depends on unknown phase '$depName'"),
        );
        visit(depPhase);
      }
      path.remove(phase.name);
      
      visited.add(phase.name);
      sorted.add(phase);
    }

    for (final phase in phases) {
      visit(phase);
    }

    return sorted;
  }
}

class _AdapterPhase implements GeneratorPhase {
  @override
  final String name;
  @override
  final String version;
  @override
  final List<String> dependsOn;
  @override
  final bool isCritical;
  
  final bool isIRNative;
  final dynamic generator;
  final AppModel model;
  final DependencyGraph graph;

  _AdapterPhase(this.name, this.generator, this.model, this.graph, {
    this.version = "1.0.0", 
    this.dependsOn = const [],
    this.isCritical = true,
    this.isIRNative = false,
  });

  @override
  Future<void> run(ProjectIR ir) async {
    if (isIRNative) {
      // Execute using the new ProjectIR contract
      await generator.generateFromIR(ir);
    } else {
      // Fallback to legacy contract (AppModel + Graph)
      if (generator.runtimeType.toString().contains('Generator')) {
        try {
          generator.graph = graph;
        } catch (_) {}
        await generator.generate(model);
      }
    }
  }
}
