import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class EmptyStateBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
Center(
  child: Column(
    mainAxisAlignment: MainAxisAlignment.center,
    children: [
      const Icon(Icons.inbox, size: 80, color: Colors.grey),
      const SizedBox(height: 16),
      const Text("No data found", style: TextStyle(fontSize: 18, color: Colors.grey)),
      ${children.join(', ')},
    ],
  ),
)
""";
  }
}
