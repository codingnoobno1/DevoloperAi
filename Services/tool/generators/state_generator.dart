import '../core/dependency_graph.dart';
import '../core/file_writer.dart';
import '../core/models.dart';
import '../utils/string_utils.dart';
import 'base_generator.dart';

class StateGenerator implements BaseGenerator {
  DependencyGraph? graph;

  @override
  Future<void> generate(AppModel model) async {
    final dg = graph ?? DependencyGraph()..resolve(model);

    for (var feature in dg.allFeatures) {
      final className = StringUtils.toPascalCase(feature);

      if (dg.stateFeatures.contains(feature)) {
        final config = model.state[feature] as Map<String, dynamic>;
        _generateState(feature, className, config);
        _generateCubit(feature, className, config, model);
      } else {
        _generateDefaultState(feature, className);
        _generateDefaultCubit(feature, className);
      }
    }
  }

  void _generateState(String feature, String className, Map<String, dynamic> config) {
    final fields = config.entries.map((e) {
      final type = e.value as String;
      final name = e.key;
      return "  final $type? $name;";
    }).join("\n");

    final constructorParams = config.keys.map((k) => "this.$k").join(", ");

    final copyWithParams = config.entries.map((e) {
      final type = e.value as String;
      final name = e.key;
      return "$type? $name";
    }).join(", ");

    final copyWithBody = config.keys.map((k) => "$k: $k ?? this.$k").join(", ");

    final code = """
class ${className}State {
$fields

  ${className}State({$constructorParams});

  ${className}State copyWith({$copyWithParams}) {
    return ${className}State(
      $copyWithBody
    );
  }
}
""";
    FileWriter.write('lib_gen/features/$feature/bloc/${feature}_state.dart', code);
  }

  void _generateCubit(String feature, String className, Map<String, dynamic> config, AppModel model) {
    final repoName = "${className}Repository";

    final code = """
import 'package:flutter_bloc/flutter_bloc.dart';
import '${feature}_state.dart';
import '../repository/${feature}_repository.dart';

class ${className}Cubit extends Cubit<${className}State> {
  final $repoName repo;

  ${className}Cubit(this.repo) : super(${className}State());

  Future<void> load() async {
    try {
      final data = await repo.getData();
    } catch (e) {
      print(e);
    }
  }
}
""";
    FileWriter.write('lib_gen/features/$feature/bloc/${feature}_cubit.dart', code);
  }

  void _generateDefaultState(String feature, String className) {
    final code = """
class ${className}State {
  final bool loading;

  ${className}State({this.loading = false});

  ${className}State copyWith({bool? loading}) {
    return ${className}State(
      loading: loading ?? this.loading,
    );
  }
}
""";
    FileWriter.write('lib_gen/features/$feature/bloc/${feature}_state.dart', code);
  }

  void _generateDefaultCubit(String feature, String className) {
    final repoName = "${className}Repository";

    final code = """
import 'package:flutter_bloc/flutter_bloc.dart';
import '${feature}_state.dart';
import '../repository/${feature}_repository.dart';

class ${className}Cubit extends Cubit<${className}State> {
  final $repoName repo;

  ${className}Cubit(this.repo) : super(${className}State());

  Future<void> load() async {
    try {
      emit(state.copyWith(loading: true));
      final data = await repo.getData();
      emit(state.copyWith(loading: false));
    } catch (e) {
      emit(state.copyWith(loading: false));
      print(e);
    }
  }
}
""";
    FileWriter.write('lib_gen/features/$feature/bloc/${feature}_cubit.dart', code);
  }
}
