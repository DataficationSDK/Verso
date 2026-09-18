import * as assert from "assert";
import {
  TextMateRule,
  buildMonacoRules,
  customizationRules,
  parseJsonc,
  resolveScope,
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
});
