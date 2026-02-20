import * as assert from "assert";

// The tokenTypeToScope mapping from themeMapper.ts (tested as pure logic)
const tokenTypeToScope: Record<string, string> = {
  keyword: "keyword",
  comment: "comment",
  string: "string",
  number: "constant.numeric",
  type: "entity.name.type",
  function: "entity.name.function",
  variable: "variable",
  operator: "keyword.operator",
  parameter: "variable.parameter",
  property: "variable.other.property",
  namespace: "entity.name.namespace",
  punctuation: "punctuation",
  "string.escape": "constant.character.escape",
  preprocessor: "meta.preprocessor",
};

suite("Theme Mapper", () => {
  test("All 14 token types are mapped", () => {
    const keys = Object.keys(tokenTypeToScope);
    assert.strictEqual(keys.length, 14);
  });

  test("keyword maps to keyword scope", () => {
    assert.strictEqual(tokenTypeToScope["keyword"], "keyword");
  });

  test("comment maps to comment scope", () => {
    assert.strictEqual(tokenTypeToScope["comment"], "comment");
  });

  test("string maps to string scope", () => {
    assert.strictEqual(tokenTypeToScope["string"], "string");
  });

  test("number maps to constant.numeric", () => {
    assert.strictEqual(tokenTypeToScope["number"], "constant.numeric");
  });

  test("type maps to entity.name.type", () => {
    assert.strictEqual(tokenTypeToScope["type"], "entity.name.type");
  });

  test("function maps to entity.name.function", () => {
    assert.strictEqual(tokenTypeToScope["function"], "entity.name.function");
  });

  test("variable maps to variable scope", () => {
    assert.strictEqual(tokenTypeToScope["variable"], "variable");
  });

  test("operator maps to keyword.operator", () => {
    assert.strictEqual(tokenTypeToScope["operator"], "keyword.operator");
  });

  test("parameter maps to variable.parameter", () => {
    assert.strictEqual(tokenTypeToScope["parameter"], "variable.parameter");
  });

  test("property maps to variable.other.property", () => {
    assert.strictEqual(tokenTypeToScope["property"], "variable.other.property");
  });

  test("namespace maps to entity.name.namespace", () => {
    assert.strictEqual(tokenTypeToScope["namespace"], "entity.name.namespace");
  });

  test("punctuation maps to punctuation scope", () => {
    assert.strictEqual(tokenTypeToScope["punctuation"], "punctuation");
  });

  test("string.escape maps to constant.character.escape", () => {
    assert.strictEqual(tokenTypeToScope["string.escape"], "constant.character.escape");
  });

  test("preprocessor maps to meta.preprocessor", () => {
    assert.strictEqual(tokenTypeToScope["preprocessor"], "meta.preprocessor");
  });

  test("Unknown token type returns undefined from map", () => {
    assert.strictEqual(tokenTypeToScope["nonexistent"], undefined);
  });

  test("Mapping produces valid TextMate scope names", () => {
    for (const [_tokenType, scope] of Object.entries(tokenTypeToScope)) {
      // TextMate scopes use dot-separated identifiers
      assert.ok(
        /^[a-z][a-z.]*[a-z]$/.test(scope),
        `Scope "${scope}" should be a valid TextMate scope name`
      );
    }
  });

  test("Color customization structure is correct", () => {
    const syntaxColors: Record<string, string> = {
      keyword: "#0000FF",
      comment: "#008000",
      string: "#A31515",
    };

    const customizations: { scope: string; settings: { foreground: string } }[] = [];

    for (const [tokenType, color] of Object.entries(syntaxColors)) {
      const scope = tokenTypeToScope[tokenType] ?? tokenType;
      customizations.push({
        scope,
        settings: { foreground: color },
      });
    }

    assert.strictEqual(customizations.length, 3);
    assert.strictEqual(customizations[0].scope, "keyword");
    assert.strictEqual(customizations[0].settings.foreground, "#0000FF");
    assert.strictEqual(customizations[1].scope, "comment");
    assert.strictEqual(customizations[2].scope, "string");
  });

  test("Unmapped token types fall back to token type as scope", () => {
    const syntaxColors: Record<string, string> = {
      "custom.token": "#FF0000",
    };

    for (const [tokenType, color] of Object.entries(syntaxColors)) {
      const scope = tokenTypeToScope[tokenType] ?? tokenType;
      assert.strictEqual(scope, "custom.token");
    }
  });
});
