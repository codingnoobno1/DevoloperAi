import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class InfiniteScrollListBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
ListView.builder(
  itemBuilder: (context, index) {
    if (index >= 100) return null;
    return const ListTile(title: Text("Dynamic Item"));
  },
)
""";
  }
}
