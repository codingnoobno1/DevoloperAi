import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class ProfileEditFormBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
SingleChildScrollView(
  padding: const EdgeInsets.all(16),
  child: Column(
    children: [
      const CircleAvatar(radius: 50, child: Icon(Icons.camera_alt)),
      const SizedBox(height: 20),
      ${children.join(', ')},
      const SizedBox(height: 40),
      ElevatedButton(onPressed: () {}, child: const Text("Save Changes")),
    ],
  ),
)
""";
  }
}
