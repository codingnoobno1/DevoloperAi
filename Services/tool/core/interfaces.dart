import 'ast.dart';
import 'models.dart';

abstract class Generator {
  Future<void> generate(AppModel model);
}

abstract class WidgetBuilder {
  WidgetNode build(WidgetModel config, AppModel model);
}

abstract class LayoutBuilder {
  WidgetNode build(LayoutModel config, List<WidgetNode> children, AppModel model);
}

abstract class TemplateBuilder {
  WidgetNode build(Map<String, dynamic> props, List<WidgetNode> children, AppModel model);
}
