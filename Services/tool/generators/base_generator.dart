import '../core/models.dart';

abstract class BaseGenerator {
  Future<void> generate(AppModel model);
}
