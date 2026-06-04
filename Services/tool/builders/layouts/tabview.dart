class TabViewBuilder {
  static String build() {
    return "DefaultTabController(length: 2, child: Scaffold(appBar: AppBar(bottom: const TabBar(tabs: [Tab(text: 'Tab 1'), Tab(text: 'Tab 2')])), body: const TabBarView(children: [Center(child: Text('Content 1')), Center(child: Text('Content 2'))])))";
  }
}
