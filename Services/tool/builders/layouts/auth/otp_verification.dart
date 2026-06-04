import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class OtpVerificationBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
Padding(
  padding: const EdgeInsets.all(24),
  child: Column(
    children: [
      const Text("Verification", style: TextStyle(fontSize: 24, fontWeight: FontWeight.bold)),
      const SizedBox(height: 20),
      Row(mainAxisAlignment: MainAxisAlignment.spaceEvenly, children: List.generate(4, (i) => Container(width: 50, height: 50, decoration: BoxDecoration(border: Border.all(color: Colors.grey), borderRadius: BorderRadius.circular(8)), child: const Center(child: TextField(textAlign: TextAlign.center, decoration: InputDecoration(border: InputBorder.none)))))),
      const SizedBox(height: 40),
      ${children.join(', ')},
    ],
  ),
)
""";
  }
}
