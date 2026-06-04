import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class StoryViewBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
SizedBox(
  height: 100,
  child: ListView.builder(
    scrollDirection: Axis.horizontal,
    itemBuilder: (context, index) => Padding(padding: const EdgeInsets.all(8), child: CircleAvatar(radius: 35, backgroundColor: Colors.red, child: CircleAvatar(radius: 32, backgroundImage: NetworkImage("https://via.placeholder.com/150")))),
  ),
)
""";
  }
}
