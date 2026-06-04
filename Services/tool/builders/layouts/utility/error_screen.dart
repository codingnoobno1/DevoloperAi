import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class ErrorScreenBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
Center(
  child: Padding(
    padding: const EdgeInsets.all(32),
    child: Column(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        const Icon(Icons.error_outline, size: 80, color: Colors.red),
        const SizedBox(height: 16),
        const Text("Something went wrong", style: TextStyle(fontSize: 20, fontWeight: FontWeight.bold)),
        const Text("Please try again later.", textAlign: TextAlign.center),
        const SizedBox(height: 32),
        ElevatedButton(onPressed: () {}, child: const Text("Retry")),
      ],
    ),
  ),
)
""";
  }
}
