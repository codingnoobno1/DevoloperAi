import 'models.dart';

class Resolver {
  static void resolve(AppModel model) {
    print("🔗 Resolving and linking AppModel references...");

    // Validate Screens -> Layouts
    for (var screen in model.screens) {
      if (!model.layouts.containsKey(screen.layout)) {
        throw Exception("❌ Resolver Error: Screen '\${screen.name}' references missing layout: '\${screen.layout}'");
      }
    }

    // Validate Layouts -> Widgets
    model.layouts.forEach((layoutName, layout) {
      for (var childKey in layout.children) {
        if (!model.widgets.containsKey(childKey)) {
          // In tabbed layouts, slots can reference other screens
          if (layout.type == 'tabs') {
            final screenExists = model.screens.any((s) => s.id == childKey);
            if (screenExists) continue;
          }
          throw Exception("❌ Resolver Error: Layout '$layoutName' references missing widget: '$childKey'");
        }
      }
    });

    // Validate Widgets -> Sub-Widgets (e.g., Forms)
    model.widgets.forEach((widgetName, widget) {
      if (widget.type == "Form") {
        final children = widget.properties['children'] as List?;
        if (children != null) {
          for (var childKey in children) {
            if (!model.widgets.containsKey(childKey)) {
              throw Exception("❌ Resolver Error: Widget '\$widgetName' (Form) references missing sub-widget: '\$childKey'");
            }
          }
        }
      }
    });

    print("✅ Resolver: All references successfully linked.");
  }
}
