import 'ast.dart';
import 'models.dart';

/// Validates the high-level AppModel configuration.
class Validator {
  static void validate(AppModel model) {
    if (model.screens.isEmpty) {
      throw Exception("❌ AppModel Validation Error: No screens defined in config.");
    }
    
    // Check if entry screen exists
    final entry = model.condition['entry']?['true'] ?? model.navigation.entry;
    bool entryExists = model.screens.any((s) => s.name == entry);
    if (!entryExists) {
      print("⚠️ Warning: Entry screen '$entry' not found in screen list. Falling back to first screen.");
    }

    // Validate layout references
    for (var screen in model.screens) {
      if (screen.layout.isNotEmpty && !model.layouts.containsKey(screen.layout)) {
        throw Exception("❌ AppModel Validation Error: Screen '${screen.name}' references unknown layout '${screen.layout}'.");
      }
    }
  }
}

/// Validates the structural integrity of the AST.
/// Stops "impossible" or "illegal" UI configurations early.
class ASTValidator {
  static void validate(WidgetNode node) {
    if (node is ColumnNode) {
      if (node.children.isEmpty) {
        throw Exception("❌ AST Validation Error: Column node must have at least one child.");
      }
      for (var child in node.children) {
        validate(child);
      }
    }

    if (node is RowNode) {
      if (node.children.isEmpty) {
        throw Exception("❌ AST Validation Error: Row node must have at least one child.");
      }
      for (var child in node.children) {
        validate(child);
      }
    }

    if (node is ButtonNode) {
      if (node.text.isEmpty) {
        throw Exception("❌ AST Validation Error: Button must have a non-empty label.");
      }
      if (node.action.isEmpty) {
        throw Exception("❌ AST Validation Error: Button must have an action assigned.");
      }
    }

    if (node is PaddingNode) {
      validate(node.child);
    }

    if (node is CenterNode) {
      validate(node.child);
    }

    if (node is ScaffoldNode) {
      if (node.body == null && node.appBar == null) {
        throw Exception("❌ AST Validation Error: Scaffold must have at least a body or an appBar.");
      }
      if (node.body != null) validate(node.body!);
      if (node.appBar != null) validate(node.appBar!);
    }

    if (node is ContainerNode) {
      if (node.child != null) validate(node.child!);
    }
  }
}
