import '../builders/registry.dart';
import '../core/ast.dart';
import '../core/renderer.dart';
import '../core/enhancer.dart';
import '../core/validator.dart';
import '../core/dependency_graph.dart';
import '../core/file_writer.dart';
import '../core/import_manager.dart';
import '../core/models.dart';
import '../utils/string_utils.dart';
import 'base_generator.dart';

class ScreenGenerator implements BaseGenerator {
  DependencyGraph? graph;

  @override
  Future<void> generate(AppModel model) async {
    final dg = graph ?? DependencyGraph()..resolve(model);

    for (var screen in model.screens) {
      final screenName = screen.name.toLowerCase();
      final className = StringUtils.toPascalCase(screen.name);
      final featureName = dg.screenToFeature[screenName] ?? screenName;

      _generateView(screenName, featureName, className, screen, model, dg);
      _generateLayoutFile(screenName, featureName, screen.layout, model, dg);
    }
  }

  void _generateView(String screenName, String feature, String className, ScreenModel screen, AppModel model, DependencyGraph dg) {
    final layoutName = screen.layout;
    final layoutClassName = StringUtils.toPascalCase(layoutName);

    final imports = ImportManager();
    imports.addPackage('flutter/material.dart');
    imports.addPackage('flutter_bloc/flutter_bloc.dart');
    imports.add('../../../core/ui/ui_factory.dart');
    imports.add('../../../core/ui/ui_adapter.dart');
    imports.add('../../../core/theme/design_tokens.dart');
    imports.add('../bloc/${feature}_cubit.dart');
    imports.add('../widgets/${layoutName.toLowerCase()}.dart');

    String body;
    if (screen.type == 'tab') {
      body = "const $layoutClassName()";
    } else {
      body = """Scaffold(
      appBar: AppBar(title: Text("${StringUtils.capitalize(screenName)}")),
      body: const $layoutClassName(),
    )""";
    }

    final code = """
${imports.build()}

class ${className}Screen extends StatelessWidget {
  const ${className}Screen({super.key});

  @override
  Widget build(BuildContext context) {
    return $body;
  }
}
""";
    FileWriter.write('lib_gen/features/$feature/view/${screenName}_screen.dart', code);
  }

  void _generateLayoutFile(String screenName, String feature, String layoutName, AppModel model, DependencyGraph dg) {
    final layout = model.layouts[layoutName];
    if (layout == null) return;

    final className = StringUtils.toPascalCase(layoutName);
    List<String> childrenDeclarations = [];
    final imports = ImportManager();
    imports.addPackage('flutter/material.dart');
    imports.add('../../../core/ui/ui_factory.dart');
    imports.add('../../../core/ui/ui_adapter.dart');
    imports.add('../../../core/theme/design_tokens.dart');

    for (var widgetKey in layout.children) {
      final widgetName = widgetKey.toLowerCase();
      
      // Check if this child is actually a Screen (common in tabbed layouts)
      final isScreen = model.screens.any((s) => s.id.toLowerCase() == widgetName);
      
      if (isScreen) {
        final screenData = model.screens.firstWhere((s) => s.id.toLowerCase() == widgetName);
        final tabFeature = dg.screenToFeature[widgetName] ?? widgetName;
        final screenClassName = StringUtils.toPascalCase(screenData.id) + "Screen";
        
        childrenDeclarations.add("const $screenClassName()");
        
        if (tabFeature == feature) {
           imports.add("${widgetName}_screen.dart");
        } else {
           imports.add("../../$tabFeature/view/${widgetName}_screen.dart");
        }
      } else {
        final widgetClassName = StringUtils.toPascalCase(widgetKey);
        childrenDeclarations.add("const $widgetClassName()");
        
        // Local widget
        imports.add('${widgetKey.toLowerCase()}.dart');
        _generateWidgetFile(feature, widgetKey, model);
      }
    }

    final layoutNode = BuilderRegistry.buildLayout(layout, childrenDeclarations.map((c) => RawNode(c)).toList(), model);
    final layoutDart = _processNode(layoutNode);

    final code = """
${imports.build()}

class $className extends StatelessWidget {
  const $className({super.key});

  @override
  Widget build(BuildContext context) {
    return $layoutDart;
  }
}
""";
    FileWriter.write('lib_gen/features/$feature/widgets/${layoutName.toLowerCase()}.dart', code);
  }

  void _generateWidgetFile(String feature, String widgetKey, AppModel model) {
    final widget = model.widgets[widgetKey];
    if (widget == null) return;

    final className = StringUtils.toPascalCase(widgetKey);
    final fileName = widgetKey.toLowerCase();

    final imports = ImportManager();
    imports.addPackage('flutter/material.dart');
    imports.add('../../../core/ui/ui_factory.dart');
    imports.add('../../../core/ui/ui_adapter.dart');
    imports.add('../../../core/theme/design_tokens.dart');

    String dartCode = "";
    if (widget.type == "Form") {
      List<String> formChildren = [];
      for (var childKey in widget.childKeys) {
        final childClassName = StringUtils.toPascalCase(childKey);
        formChildren.add("const $childClassName()");
        imports.add('${childKey.toLowerCase()}.dart');
        _generateWidgetFile(feature, childKey, model);
      }
      final childrenList = formChildren.join(', ');
      dartCode = "Column(children: [$childrenList])";
    } else {
      final widgetNode = BuilderRegistry.buildWidget(widget, model);
      dartCode = _processNode(widgetNode);
    }

    final code = """
${imports.build()}

class $className extends StatelessWidget {
  const $className({super.key});

  @override
  Widget build(BuildContext context) {
    return $dartCode;
  }
}
""";
    FileWriter.write('lib_gen/features/$feature/widgets/$fileName.dart', code);
  }
  String _processNode(WidgetNode node) {
    // 1. Build AST (already done by builders)
    
    // 2. Enhance
    final enhanced = ASTEnhancer.enhance(node);
    
    // 3. Validate
    ASTValidator.validate(enhanced);
    
    // 4. Render
    return DartRenderer.render(enhanced);
  }
}
