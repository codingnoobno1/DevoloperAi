import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class DashboardCardGridBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
GridView.count(
  crossAxisCount: 2,
  padding: const EdgeInsets.all(16),
  mainAxisSpacing: 16,
  crossAxisSpacing: 16,
  children: [${children.join(', ')}],
)
""";
  }
}
