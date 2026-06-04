import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class ChatConversationBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
Column(
  children: [
    Expanded(child: ListView(children: [const Text("Hello!"), const Text("Hi there!")])),
    Padding(padding: const EdgeInsets.all(8), child: Row(children: [const Expanded(child: TextField()), IconButton(onPressed: () {}, icon: const Icon(Icons.send))])),
  ],
)
""";
  }
}
