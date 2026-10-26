import * as assert from "assert";
import {
  TextMateRule,
  buildMonacoRules,
  buildSemanticRules,
  customizationRules,
  parseJsonc,
  resolveScope,
  semanticCustomization,
} from "../../src/blazor/themeTokenRules";

suite("Theme token rules", () => {
  const theme: TextMateRule[] = [
    { scope: "comment", settings: { foreground: "#6A9955", fontStyle: "italic" } },
    { scope: ["string", "meta.embedded.assembly"], settings: { foreground: "#CE9178" } },
    { scope: "keyword", settings: { foreground: "#569CD6" } },
    { scope: "keyword.control", settings: { foreground: "#C586C0" } },
    { scope: "keyword.operator", settings: { foreground: "#D4D4D4" } },
    { scope: "constant.language", settings: { foreground: "#D19A66" } },
    { scope: "entity.name.type, support.class", settings: { foreground: "#4EC9B0" } },
    { scope: "source.cs string", settings: { foreground: "#FF0000" } },
  ];

  const ruleFor = (rules: ReturnType<typeof buildMonacoRules>, token: string) =>
    rules.find((r) => r.token === token);

  test("parseJsonc accepts comments, trailing commas and a byte order mark", () => {
    const parsed = parseJsonc(
      '﻿{ // line\n "a": "x, ] // kept", /* block */ "b": [1, 2,], }'
    );
    assert.deepStrictEqual(parsed, { a: "x, ] // kept", b: [1, 2] });
  });

  test("the most specific selector wins, and a later rule wins a tie", () => {
    assert.strictEqual(resolveScope(theme, "keyword.control.loop.cs").foreground, "#C586C0");
    assert.strictEqual(resolveScope(theme, "keyword.other.using.cs").foreground, "#569CD6");

    const tied = [...theme, { scope: "keyword", settings: { foreground: "#111111" } }];
    assert.strictEqual(resolveScope(tied, "keyword.other.using.cs").foreground, "#111111");
  });

  test("a selector matches whole scope segments only", () => {
    const rules = [{ scope: "string", settings: { foreground: "#CE9178" } }];
    assert.strictEqual(resolveScope(rules, "stringy.thing").foreground, undefined);
  });

  test("selectors that need a parent scope are passed over", () => {
    assert.strictEqual(resolveScope(theme, "string.quoted.double.cs").foreground, "#CE9178");
  });

  test("color and font style are resolved independently", () => {
    const rules = [
      ...theme,
      { scope: "comment.block.documentation", settings: { foreground: "#608B4E" } },
    ];
    const hit = resolveScope(rules, "comment.block.documentation.cs");
    assert.strictEqual(hit.foreground, "#608B4E");
    assert.strictEqual(hit.fontStyle, "italic");
  });

  test("Monaco tokens get the color of the scope VS Code would have used", () => {
    const rules = buildMonacoRules(theme);
    assert.deepStrictEqual(ruleFor(rules, "comment"), {
      token: "comment",
      foreground: "#6A9955",
      fontStyle: "italic",
    });
    assert.strictEqual(ruleFor(rules, "string")?.foreground, "#CE9178");
    assert.strictEqual(ruleFor(rules, "type")?.foreground, "#4EC9B0");
    assert.strictEqual(ruleFor(rules, "operator")?.foreground, "#D4D4D4");
  });

  test("keywords that carry their own name recover the theme's keyword kinds", () => {
    const rules = buildMonacoRules(theme);
    assert.strictEqual(ruleFor(rules, "keyword")?.foreground, "#569CD6");
    assert.strictEqual(ruleFor(rules, "keyword.foreach")?.foreground, "#C586C0");
    assert.strictEqual(ruleFor(rules, "keyword.public")?.foreground, undefined);
    assert.strictEqual(ruleFor(rules, "keyword.true")?.foreground, "#D19A66");
  });

  test("a token the theme does not color gets no rule, and identifiers never do", () => {
    const rules = buildMonacoRules(theme);
    assert.strictEqual(ruleFor(rules, "number"), undefined);
    assert.strictEqual(ruleFor(rules, "identifier"), undefined);
  });

  test("emphasis and strong keep a font style when the theme sets none", () => {
    const rules = buildMonacoRules([]);
    assert.deepStrictEqual(ruleFor(rules, "emphasis"), { token: "emphasis", fontStyle: "italic" });
    assert.deepStrictEqual(ruleFor(rules, "strong"), { token: "strong", fontStyle: "bold" });
  });

  test("token color customizations apply globally and per theme", () => {
    const custom = customizationRules(
      {
        comments: "#FF0000",
        "[One Dark*]": { keywords: { foreground: "#00FF00", fontStyle: "bold" } },
        "[Monokai][Abyss]": { strings: "#0000FF" },
        textMateRules: [{ scope: "constant.language", settings: { foreground: "#ABCDEF" } }],
      },
      "One Dark Pro"
    );
    const rules = buildMonacoRules([...theme, ...custom]);

    assert.strictEqual(ruleFor(rules, "comment")?.foreground, "#FF0000");
    assert.strictEqual(ruleFor(rules, "keyword")?.foreground, "#00FF00");
    assert.strictEqual(ruleFor(rules, "keyword")?.fontStyle, "bold");
    assert.strictEqual(ruleFor(rules, "string")?.foreground, "#CE9178");
    assert.strictEqual(ruleFor(rules, "keyword.null")?.foreground, "#ABCDEF");
  });

  test("customizations that are missing or malformed yield nothing", () => {
    assert.deepStrictEqual(customizationRules(undefined, "Any"), []);
    assert.deepStrictEqual(customizationRules("nonsense", "Any"), []);
  });
  // A slice of Dark 2026 and the themes it includes.
  const dark2026: TextMateRule[] = [
    { scope: "entity.name.type", settings: { foreground: "#4EC9B0" } },
    { scope: "entity.name", settings: { foreground: "#FFA657" } },
    { scope: "entity.name.function", settings: { foreground: "#D2A8FF" } },
    { scope: "variable.other", settings: { foreground: "#C9D1D9" } },
    { scope: ["variable.other.enummember", "variable.other.constant"], settings: { foreground: "#79C0FF" } },
    { scope: "variable.parameter", settings: { foreground: "#9CDCFE" } },
  ];

  test("semantic tokens take the scope VS Code falls back to", () => {
    const rules = buildSemanticRules(dark2026, {});

    // entity.name.type.struct and .class fall to entity.name.type, the most specific match.
    assert.strictEqual(ruleFor(rules, "struct")?.foreground, "#4EC9B0");
    assert.strictEqual(ruleFor(rules, "class")?.foreground, "#4EC9B0");
    assert.strictEqual(ruleFor(rules, "enumMember")?.foreground, "#79C0FF");
    assert.strictEqual(ruleFor(rules, "method")?.foreground, "#D2A8FF");
    assert.strictEqual(ruleFor(rules, "property")?.foreground, "#C9D1D9");
    assert.strictEqual(ruleFor(rules, "parameter")?.foreground, "#9CDCFE");
  });

  test("semantic types the Monaco tokenizers share keep the lexical rule", () => {
    const rules = buildSemanticRules(dark2026, {});

    for (const token of ["variable", "function", "type", "namespace", "keyword", "operator"]) {
      assert.strictEqual(ruleFor(rules, token), undefined, token);
    }
  });

  test("semanticTokenColors win over the fallback scope, and name modifiers", () => {
    const rules = buildSemanticRules(dark2026, {
      enumMember: "#FF0000",
      variable: { foreground: "#00FF00", italic: true },
      "variable.mutable": { underline: true },
      "*.static": { bold: true },
      "class:fsharp": "#123456",
    });

    assert.strictEqual(ruleFor(rules, "enumMember")?.foreground, "#FF0000");
    assert.deepStrictEqual(ruleFor(rules, "variable"), { token: "variable", foreground: "#00FF00", fontStyle: "italic" });
    assert.deepStrictEqual(ruleFor(rules, "variable.mutable"), { token: "variable.mutable", fontStyle: "underline" });
    assert.strictEqual(ruleFor(rules, "method.static")?.fontStyle, "bold");
    assert.strictEqual(ruleFor(rules, "class")?.foreground, "#4EC9B0", "a language-scoped selector is passed over");
  });

  test("a semantic token the theme says nothing about gets no rule", () => {
    assert.deepStrictEqual(buildSemanticRules([], {}), []);
  });

  test("semantic token customizations apply generally and for the active theme", () => {
    const custom = semanticCustomization(
      {
        enabled: false,
        rules: { variable: "#111111" },
        "[Dark 2026]": { enabled: true, rules: { property: "#222222" } },
        "[Light 2026]": { rules: { variable: "#333333" } },
      },
      "Dark 2026"
    );

    assert.strictEqual(custom.enabled, true);
    assert.deepStrictEqual(custom.rules, { variable: "#111111", property: "#222222" });
    assert.deepStrictEqual(semanticCustomization(undefined, "Any"), { rules: {} });
  });
});
