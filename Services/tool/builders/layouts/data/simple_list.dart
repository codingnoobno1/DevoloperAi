import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class SimpleListBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
ListView(
  padding: const EdgeInsets.all(8),
  children: [${children.join(', ')}],
)
""";
  }
}
