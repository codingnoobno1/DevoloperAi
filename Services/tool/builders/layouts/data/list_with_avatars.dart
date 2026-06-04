import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class ListWithAvatarsBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
ListView.separated(
  itemCount: ${children.length},
  separatorBuilder: (context, index) => const Divider(),
  itemBuilder: (context, index) => ListTile(
    leading: const CircleAvatar(child: Icon(Icons.person)),
    title: const Text("Item Title"),
    subtitle: const Text("Item Subtitle"),
  ),
)
""";
  }
}
