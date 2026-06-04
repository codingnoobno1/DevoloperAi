import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class DashboardBottomNavBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
Scaffold(
  body: IndexedStack(index: 0, children: [${children.join(', ')}]),
  bottomNavigationBar: BottomNavigationBar(items: const [BottomNavigationBarItem(icon: Icon(Icons.home), label: 'Home'), BottomNavigationBarItem(icon: Icon(Icons.person), label: 'Profile')]),
)
""";
  }
}
