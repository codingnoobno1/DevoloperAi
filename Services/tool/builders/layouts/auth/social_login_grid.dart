import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class SocialLoginGridBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
Column(
  children: [
    ${children.join(', ')},
    const SizedBox(height: 20),
    const Text("Or login with"),
    const SizedBox(height: 20),
    Row(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        IconButton(onPressed: () {}, icon: const Icon(Icons.facebook)),
        IconButton(onPressed: () {}, icon: const Icon(Icons.g_mobiledata, size: 40)),
        IconButton(onPressed: () {}, icon: const Icon(Icons.apple)),
      ],
    ),
  ],
)
""";
  }
}
