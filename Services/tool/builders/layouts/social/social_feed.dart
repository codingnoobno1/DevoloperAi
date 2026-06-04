import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class SocialFeedBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
ListView(
  children: [
    Card(child: Column(children: [const ListTile(title: Text("User Name")), Image.network("https://via.placeholder.com/300"), const Padding(padding: EdgeInsets.all(8), child: Text("Post Content"))])),
    Card(child: Column(children: [const ListTile(title: Text("Another User")), Image.network("https://via.placeholder.com/300"), const Padding(padding: EdgeInsets.all(8), child: Text("Another Post"))])),
  ],
)
""";
  }
}
