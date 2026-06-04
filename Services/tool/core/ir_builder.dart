import 'models.dart';
import 'dependency_graph.dart';
import 'ir.dart';
import 'ast.dart';
import '../expansion/ast_interpreter.dart';
import '../utils/logger.dart';

/// IRBuilder v6.5: Resilient Semantic Analyzer.
/// Features improved theme resolution and fail-safe dependency mapping.
class IRBuilder {
  static ProjectIR build(AppModel model, DependencyGraph graph) {
    final ir = ProjectIR();
    ir.metadata = {
      "version": "6.5.0-ir",
      "timestamp": DateTime.now().toIso8601String(),
      "entry": model.condition['entry']?['true'] ?? model.navigation.entry,
    };

    // 1. Build Theme IR (Enhanced Resolution)
    // Try top-level tokens, then fallback to light-theme tokens
    ir.theme.tokens = model.theme['tokens'] ?? model.theme['light']?['tokens'] ?? {};
    ir.theme.components = model.theme['components'] ?? {};
    ir.theme.lightColors = model.theme['light']?['colors'] ?? model.theme['themes']?['light']?['colors'] ?? {};
    ir.theme.darkColors = model.theme['dark']?['colors'] ?? model.theme['themes']?['dark']?['colors'] ?? {};
    
    if (ir.theme.tokens.isEmpty && model.theme.isNotEmpty) {
      // If we still have no tokens but we HAVE a theme, it might be a structure mismatch
      ir.addError("Theme tokens missing. Expected 'tokens' at top-level or under 'light'.");
    }
    ir.theme.isValid = !ir.hasErrors;

    // 2. Build Services IR
    model.api.forEach((k, v) => ir.services[k] = ServiceIR(k, 'api', v));
    model.db.forEach((k, v) => ir.services[k] = ServiceIR(k, 'db', v));

    // 3. Build Features IR
    for (final feature in graph.allFeatures) {
      final fIR = FeatureIR(
        feature,
        bloc: model.bloc[feature] ?? {},
        repository: model.repository[feature] ?? {},
      );

      final screenNames = graph.featureScreens[feature] ?? [];
      for (final sName in screenNames) {
        final sModel = _findScreen(model, sName);
        if (sModel == null) {
          ir.addError("Screen reference broken: '$sName' is in graph but not in config.");
          continue;
        }

        final route = model.navigation.routes[sName] ?? '/$sName';
        final sIR = ScreenIR(sName, route, sModel.type ?? 'sub');
        
        final layoutModel = model.layouts[sModel.layout];
        if (layoutModel != null) {
          if (layoutModel.ast != null) {
            try {
              sIR.root = ASTInterpreter.interpret(layoutModel.ast!, layoutModel.props);
            } catch (e) {
              ir.addError("Semantic Error on screen '$sName': $e");
            }
          } else {
            // New Structured Architecture: Layout is defined by type/template + children/slots
            sIR.root = RawNode("// Synthesized Root for ${layoutModel.type} template");
          }
        } else {
          ir.addError("Layout missing for screen '$sName': ${sModel.layout}");
        }

        fIR.screens.add(sIR);
      }

      // Feature-specific widgets
      model.widgets.forEach((k, v) {
        if (k.startsWith(feature)) {
          if (v.ast != null) {
            try {
              fIR.widgetASTs[k] = ASTInterpreter.interpret(v.ast!, v.properties);
            } catch (e) {
              ir.addError("Semantic Error in widget AST: $k -> $e");
            }
          } else {
            fIR.widgetASTs[k] = RawNode("// Synthesized Widget: ${v.type}");
          }
        }
      });

      ir.features[feature] = fIR;
    }

    if (ir.hasErrors) {
      Logger.error("IR Semantic Analysis failed with ${ir.errors.length} errors.");
    }

    return ir;
  }

  static ScreenModel? _findScreen(AppModel model, String name) {
    try {
      return model.screens.firstWhere((s) => s.name == name);
    } catch (_) {
      return null;
    }
  }
}
