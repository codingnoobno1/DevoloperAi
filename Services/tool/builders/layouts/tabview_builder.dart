import '../../core/interfaces.dart';
import '../../core/models.dart';

class TabViewLayoutBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return "DefaultTabController(length: 2, child: Scaffold(appBar: AppBar(bottom: const TabBar(tabs: [Tab(text: 'Tab 1'), Tab(text: 'Tab 2')])), body: const TabBarView(children: [Center(child: Text('Content 1')), Center(child: Text('Content 2'))])))";
  }
}
