---
id: spring
label: Spring Boot
language: Java
kind: Backend
version: 1.0.0
packageManager: gradle
defaultPort: 8080
architectures: [flat]
defaultArchitecture: flat
placeholders:
  - { key: ProjectName, prompt: "Project name", default: "myapp" }
  - { key: Port,        prompt: "HTTP port",     default: 8080, type: int }
provides: [API_BASE_URL]
needs: [DATABASE_URL]
run: "./gradlew bootRun"
tags: [api, java, spring]
---

# Spring Boot Template

A minimal Spring Boot service with a health/hello controller.

## Shared

### file: README.md
```markdown
# {{ProjectName}}

Spring Boot service scaffolded by Syncro.

## Run
```
./gradlew bootRun
```
Open http://localhost:{{Port}}/api/health
```

### file: .gitignore
```text
.gradle/
build/
*.class
.env
.syncro_db/
```

### file: settings.gradle
```groovy
rootProject.name = '{{ProjectName}}'
```

## Architecture: flat

### file: build.gradle
```groovy
plugins {
    id 'java'
    id 'org.springframework.boot' version '3.3.2'
    id 'io.spring.dependency-management' version '1.1.6'
}

group = 'com.example'
version = '1.0.0'
java { sourceCompatibility = '17' }

repositories { mavenCentral() }

dependencies {
    implementation 'org.springframework.boot:spring-boot-starter-web'
}
```

### file: src/main/resources/application.properties
```text
spring.application.name={{ProjectName}}
server.port={{Port}}
```

### file: src/main/java/com/example/demo/DemoApplication.java
```java
package com.example.demo;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;

@SpringBootApplication
public class DemoApplication {
    public static void main(String[] args) {
        SpringApplication.run(DemoApplication.class, args);
    }
}
```

### file: src/main/java/com/example/demo/ApiController.java
```java
package com.example.demo;

import org.springframework.web.bind.annotation.*;
import java.util.*;

@RestController
@RequestMapping("/api")
public class ApiController {

    private final List<Map<String, Object>> items = new ArrayList<>();

    @GetMapping("/health")
    public Map<String, String> health() {
        return Map.of("status", "healthy");
    }

    @GetMapping("/items")
    public List<Map<String, Object>> list() {
        return items;
    }

    @PostMapping("/items")
    public Map<String, Object> create(@RequestBody Map<String, Object> item) {
        item.put("id", items.size() + 1);
        items.add(item);
        return item;
    }
}
```
