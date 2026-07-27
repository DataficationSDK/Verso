"""Cell compilation, result selection, and scope behaviour."""

import inspect

import versohost


# --- splitting ---

def test_split_separates_statements_from_a_trailing_expression():
    statements, expression = versohost.split_cell("x = 1\ny = 2\nx + y")

    assert statements is not None
    assert expression is not None

    scope = versohost.new_scope()
    exec(statements, scope)
    assert eval(expression, scope) == 3


def test_split_returns_no_statements_for_a_lone_expression():
    statements, expression = versohost.split_cell("1 + 1")

    assert statements is None
    assert eval(expression, versohost.new_scope()) == 2


def test_split_returns_no_expression_when_the_cell_ends_in_a_statement():
    statements, expression = versohost.split_cell("x = 1")

    assert statements is not None
    assert expression is None


def test_split_handles_an_empty_cell():
    assert versohost.split_cell("# just a comment\n") == (None, None)


def test_split_keeps_a_multi_line_expression_whole():
    # The line-based heuristic this replaces saw only the closing fragment of such an expression.
    _, expression = versohost.split_cell("(1 +\n 2 +\n 3)")

    assert eval(expression, versohost.new_scope()) == 6


def test_split_marks_a_top_level_await_expression_as_a_coroutine():
    _, expression = versohost.split_cell("import asyncio\nawait asyncio.sleep(0)")

    assert expression.co_flags & inspect.CO_COROUTINE


def test_split_marks_a_top_level_await_statement_as_a_coroutine():
    statements, _ = versohost.split_cell("import asyncio\nawait asyncio.sleep(0)\n42")

    assert statements.co_flags & inspect.CO_COROUTINE


def test_a_trailing_semicolon_suppresses_the_result():
    assert versohost.suppresses_result("1 + 1;")
    assert versohost.suppresses_result("1 + 1;  \n")
    assert not versohost.suppresses_result("1 + 1")


# --- results ---

def test_result_carries_the_repr_of_the_last_expression(execute):
    result = execute("40 + 2")

    assert result["status"] == "ok"
    assert result["result"] == {"mime": "text/plain", "data": "42"}


def test_a_statement_only_cell_has_no_result(execute):
    assert execute("x = 1")["result"] is None


def test_a_none_valued_expression_has_no_result(execute):
    assert execute("print")["result"] is not None
    assert execute("None")["result"] is None


def test_a_suppressed_result_is_not_reported(execute):
    assert execute("40 + 2;")["result"] is None


def test_a_rich_object_reports_its_html(execute):
    result = execute("class T:\n    def _repr_html_(self):\n        return '<b>rich</b>'\nT()")

    assert result["result"] == {"mime": "text/html", "data": "<b>rich</b>"}


def test_a_failing_html_repr_falls_back_to_plain_text(execute):
    result = execute(
        "class T:\n"
        "    def _repr_html_(self):\n"
        "        raise RuntimeError('broken')\n"
        "    def __repr__(self):\n"
        "        return 'fallback'\n"
        "T()")

    assert result["result"] == {"mime": "text/plain", "data": "fallback"}


# --- scope ---

def test_scope_persists_between_cells(execute):
    execute("saved = 41")

    assert execute("saved + 1")["result"]["data"] == "42"


def test_scope_reports_itself_as_main(execute):
    assert execute("__name__")["result"]["data"] == "'__main__'"


def test_functions_defined_in_one_cell_are_callable_in_the_next(execute):
    execute("def double(value):\n    return value * 2")

    assert execute("double(21)")["result"]["data"] == "42"


# --- asynchronous cells ---

def test_top_level_await_runs_to_completion(execute):
    result = execute("import asyncio\nawait asyncio.sleep(0.01)\n'done'")

    assert result["status"] == "ok"
    assert result["result"]["data"] == "'done'"


def test_an_awaited_trailing_expression_reports_its_value(execute):
    execute("import asyncio\nasync def answer():\n    return 42")

    assert execute("await answer()")["result"]["data"] == "42"


def test_tasks_survive_between_cells(execute):
    # One event loop for the process means a task started in one cell can be awaited in another.
    execute(
        "import asyncio\n"
        "async def slow():\n"
        "    await asyncio.sleep(0.05)\n"
        "    return 'later'\n"
        "pending = asyncio.ensure_future(slow())")

    assert execute("await pending")["result"]["data"] == "'later'"


# --- bootstrap ---

def test_bootstrap_imports_are_bound_in_the_scope(session, execute):
    session.apply_bootstrap({"default_imports": ["json"]})

    assert execute("json.dumps([1])")["result"]["data"] == "'[1]'"


def test_a_missing_bootstrap_import_is_skipped(session, execute, channel):
    session.apply_bootstrap({"default_imports": ["json", "verso_module_that_does_not_exist"]})

    assert channel.of_type("stream") == []
    assert execute("json.dumps([])")["status"] == "ok"


def test_bootstrap_startup_code_runs_in_the_scope(session, execute):
    session.apply_bootstrap({"startup_code": "greeting = 'hello'"})

    assert execute("greeting")["result"]["data"] == "'hello'"


def test_failing_startup_code_is_reported_without_stopping_the_host(session, execute, channel):
    session.apply_bootstrap({"startup_code": "raise RuntimeError('bad startup')"})

    reported = channel.stream_text("stderr")
    assert "bad startup" in reported

    # The session is still usable despite the failure.
    assert execute("1")["status"] == "ok"
