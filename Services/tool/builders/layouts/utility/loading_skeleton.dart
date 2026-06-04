import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class LoadingSkeletonBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
ListView.builder(
  itemCount: 10,
  itemBuilder: (context, index) => Padding(
    padding: const EdgeInsets.all(8.0),
    child: Row(
      children: [
        Container(width: 50, height: 50, color: Colors.grey.shade200),
        const SizedBox(width: 16),
        Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [Container(height: 10, color: Colors.grey.shade200), const SizedBox(height: 5), Container(height: 10, width: 100, color: Colors.grey.shade200)])),
      ],
    ),
  ),
)
""";
  }
}
