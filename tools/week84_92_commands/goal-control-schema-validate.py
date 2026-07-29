from __future__ import annotations

from datetime import datetime
import json
from pathlib import Path
import re
from typing import Any, Mapping


SCHEMA_PATH = Path("docs_md/weekly/84_92_week_goal_control.schema.json")
STATE_PATH = Path("artifacts/week84-92-goal-control/goal-state.json")
SUBSET_PROTOCOL = "frozen-schema-subset-v1"
SUPPORTED_KEYWORDS = frozenset(
    {
        "$schema",
        "$id",
        "$defs",
        "$ref",
        "type",
        "properties",
        "required",
        "additionalProperties",
        "const",
        "enum",
        "pattern",
        "format",
        "minimum",
        "maximum",
        "minLength",
        "minItems",
        "maxItems",
        "uniqueItems",
        "items",
        "prefixItems",
        "allOf",
        "oneOf",
        "if",
        "then",
        "else",
        "title",
        "description",
    }
)
JSON_TYPES = frozenset(
    {"null", "boolean", "object", "array", "number", "integer", "string"}
)
DATE_TIME = re.compile(
    r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}"
    r"(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$"
)


class DuplicateKeyError(ValueError):
    pass


class SchemaSubsetError(ValueError):
    pass


def no_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise DuplicateKeyError(key)
        result[key] = value
    return result


def reject_nonfinite(_value: str) -> None:
    raise SchemaSubsetError("JSON_NONFINITE")


def _check_json_value(value: Any) -> None:
    if isinstance(value, str):
        try:
            value.encode("utf-8", errors="strict")
        except UnicodeEncodeError as error:
            raise SchemaSubsetError("JSON_SURROGATE") from error
    elif isinstance(value, Mapping):
        for key, child in value.items():
            if not isinstance(key, str):
                raise SchemaSubsetError("JSON_KEY")
            _check_json_value(key)
            _check_json_value(child)
    elif isinstance(value, list):
        for child in value:
            _check_json_value(child)
    elif isinstance(value, float):
        if value != value or value in {float("inf"), float("-inf")}:
            raise SchemaSubsetError("JSON_NONFINITE")
    elif value is not None and not isinstance(value, (bool, int)):
        raise SchemaSubsetError("JSON_VALUE")


def loads_json(raw: bytes) -> Any:
    value = json.loads(
        raw.decode("utf-8"),
        object_pairs_hook=no_duplicates,
        parse_constant=reject_nonfinite,
    )
    _check_json_value(value)
    # A successful strict UTF-8 serialization proves there is no hidden lone
    # surrogate or non-JSON runtime value after parsing.
    json.dumps(
        value,
        ensure_ascii=False,
        allow_nan=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8", errors="strict")
    return value


def _is_integer(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _is_number(value: Any) -> bool:
    return (isinstance(value, (int, float)) and not isinstance(value, bool))


def _json_equal(left: Any, right: Any) -> bool:
    if _is_number(left) and _is_number(right):
        return left == right
    if type(left) is not type(right):
        return False
    if isinstance(left, Mapping):
        return set(left) == set(right) and all(
            _json_equal(left[key], right[key]) for key in left
        )
    if isinstance(left, list):
        return len(left) == len(right) and all(
            _json_equal(a, b) for a, b in zip(left, right)
        )
    return left == right


def _decode_pointer_token(token: str) -> str:
    result: list[str] = []
    index = 0
    while index < len(token):
        if token[index] != "~":
            result.append(token[index])
            index += 1
            continue
        if index + 1 >= len(token) or token[index + 1] not in {"0", "1"}:
            raise SchemaSubsetError("SCHEMA_REF_ESCAPE")
        result.append("~" if token[index + 1] == "0" else "/")
        index += 2
    return "".join(result)


def _resolve_ref(root: Mapping[str, Any], reference: Any) -> Any:
    if not isinstance(reference, str) or not reference.startswith("#/"):
        raise SchemaSubsetError("SCHEMA_REF_SCOPE")
    current: Any = root
    for encoded in reference[2:].split("/"):
        token = _decode_pointer_token(encoded)
        if isinstance(current, Mapping) and token in current:
            current = current[token]
        elif isinstance(current, list) and token.isdigit() and int(token) < len(current):
            current = current[int(token)]
        else:
            raise SchemaSubsetError("SCHEMA_REF_TARGET")
    if not isinstance(current, (Mapping, bool)):
        raise SchemaSubsetError("SCHEMA_REF_TARGET")
    return current


def _check_schema_node(
    schema: Any,
    root: Mapping[str, Any],
    active: set[int],
) -> None:
    if isinstance(schema, bool):
        return
    if not isinstance(schema, Mapping):
        raise SchemaSubsetError("SCHEMA_NODE")
    identity = id(schema)
    if identity in active:
        raise SchemaSubsetError("SCHEMA_RECURSION")
    active.add(identity)
    try:
        unknown = set(schema) - SUPPORTED_KEYWORDS
        if unknown:
            raise SchemaSubsetError("SCHEMA_KEYWORD")
        declared_type = schema.get("type")
        if declared_type is not None and declared_type not in JSON_TYPES:
            raise SchemaSubsetError("SCHEMA_TYPE")
        required = schema.get("required")
        if required is not None and (
            not isinstance(required, list)
            or any(not isinstance(item, str) for item in required)
            or len(set(required)) != len(required)
        ):
            raise SchemaSubsetError("SCHEMA_REQUIRED")
        properties = schema.get("properties")
        if properties is not None:
            if not isinstance(properties, Mapping) or any(
                not isinstance(key, str) for key in properties
            ):
                raise SchemaSubsetError("SCHEMA_PROPERTIES")
            for child in properties.values():
                _check_schema_node(child, root, active)
        definitions = schema.get("$defs")
        if definitions is not None:
            if not isinstance(definitions, Mapping) or any(
                not isinstance(key, str) for key in definitions
            ):
                raise SchemaSubsetError("SCHEMA_DEFS")
            for child in definitions.values():
                _check_schema_node(child, root, active)
        if "$ref" in schema:
            _resolve_ref(root, schema["$ref"])
        if "pattern" in schema:
            if not isinstance(schema["pattern"], str):
                raise SchemaSubsetError("SCHEMA_PATTERN")
            try:
                re.compile(schema["pattern"])
            except re.error as error:
                raise SchemaSubsetError("SCHEMA_PATTERN") from error
        if "format" in schema and schema["format"] != "date-time":
            raise SchemaSubsetError("SCHEMA_FORMAT")
        if "enum" in schema:
            values = schema["enum"]
            if (
                not isinstance(values, list)
                or not values
                or any(
                    _json_equal(values[left], values[right])
                    for left in range(len(values))
                    for right in range(left + 1, len(values))
                )
            ):
                raise SchemaSubsetError("SCHEMA_ENUM")
        for key in ("minimum", "maximum"):
            if key in schema and not _is_number(schema[key]):
                raise SchemaSubsetError("SCHEMA_NUMERIC_BOUND")
        for key in ("minLength", "minItems", "maxItems"):
            if key in schema and (
                not _is_integer(schema[key]) or schema[key] < 0
            ):
                raise SchemaSubsetError("SCHEMA_SIZE_BOUND")
        if "uniqueItems" in schema and not isinstance(schema["uniqueItems"], bool):
            raise SchemaSubsetError("SCHEMA_UNIQUE")
        additional = schema.get("additionalProperties")
        if additional is not None and not isinstance(additional, (bool, Mapping)):
            raise SchemaSubsetError("SCHEMA_ADDITIONAL")
        if isinstance(additional, Mapping):
            _check_schema_node(additional, root, active)
        if "items" in schema:
            _check_schema_node(schema["items"], root, active)
        prefix = schema.get("prefixItems")
        if prefix is not None:
            if not isinstance(prefix, list):
                raise SchemaSubsetError("SCHEMA_PREFIX")
            for child in prefix:
                _check_schema_node(child, root, active)
        for key in ("allOf", "oneOf"):
            branches = schema.get(key)
            if branches is not None:
                if not isinstance(branches, list) or not branches:
                    raise SchemaSubsetError("SCHEMA_COMBINATOR")
                for child in branches:
                    _check_schema_node(child, root, active)
        for key in ("if", "then", "else"):
            if key in schema:
                _check_schema_node(schema[key], root, active)
    finally:
        active.remove(identity)


def check_schema(schema: Any) -> Mapping[str, Any]:
    _check_json_value(schema)
    if not isinstance(schema, Mapping):
        raise SchemaSubsetError("SCHEMA_ROOT")
    if schema.get("$schema") != "https://json-schema.org/draft/2020-12/schema":
        raise SchemaSubsetError("SCHEMA_DIALECT")
    _check_schema_node(schema, schema, set())
    return schema


def _matches_type(instance: Any, declared: str) -> bool:
    return {
        "null": instance is None,
        "boolean": isinstance(instance, bool),
        "object": isinstance(instance, Mapping),
        "array": isinstance(instance, list),
        "number": _is_number(instance),
        "integer": _is_integer(instance),
        "string": isinstance(instance, str),
    }[declared]


def _date_time_valid(value: str) -> bool:
    if DATE_TIME.fullmatch(value) is None:
        return False
    try:
        parsed = datetime.fromisoformat(value[:-1] + "+00:00" if value.endswith("Z") else value)
    except ValueError:
        return False
    return parsed.tzinfo is not None


def _validate(
    instance: Any,
    schema: Any,
    root: Mapping[str, Any],
    path: str,
    ref_stack: tuple[str, ...],
) -> list[str]:
    if schema is True:
        return []
    if schema is False:
        return [f"{path}:FALSE_SCHEMA"]
    if not isinstance(schema, Mapping):
        return [f"{path}:SCHEMA_NODE"]
    errors: list[str] = []
    reference = schema.get("$ref")
    if reference is not None:
        if not isinstance(reference, str) or reference in ref_stack or len(ref_stack) >= 64:
            return [f"{path}:REF_CYCLE"]
        target = _resolve_ref(root, reference)
        errors.extend(_validate(instance, target, root, path, (*ref_stack, reference)))
    declared_type = schema.get("type")
    if isinstance(declared_type, str) and not _matches_type(instance, declared_type):
        return [f"{path}:TYPE"]
    if "const" in schema and not _json_equal(instance, schema["const"]):
        errors.append(f"{path}:CONST")
    if "enum" in schema:
        values = schema["enum"]
        if not isinstance(values, list) or not any(
            _json_equal(instance, value) for value in values
        ):
            errors.append(f"{path}:ENUM")
    if isinstance(instance, str):
        pattern = schema.get("pattern")
        if isinstance(pattern, str) and re.search(pattern, instance) is None:
            errors.append(f"{path}:PATTERN")
        if schema.get("format") == "date-time" and not _date_time_valid(instance):
            errors.append(f"{path}:DATE_TIME")
        minimum_length = schema.get("minLength")
        if _is_integer(minimum_length) and len(instance) < minimum_length:
            errors.append(f"{path}:MIN_LENGTH")
    if _is_number(instance):
        if _is_number(schema.get("minimum")) and instance < schema["minimum"]:
            errors.append(f"{path}:MINIMUM")
        if _is_number(schema.get("maximum")) and instance > schema["maximum"]:
            errors.append(f"{path}:MAXIMUM")
    if isinstance(instance, Mapping):
        properties = schema.get("properties")
        property_schemas = properties if isinstance(properties, Mapping) else {}
        required = schema.get("required")
        if isinstance(required, list):
            for key in required:
                if key not in instance:
                    errors.append(f"{path}:REQUIRED")
        for key, child_schema in property_schemas.items():
            if key in instance:
                errors.extend(
                    _validate(
                        instance[key],
                        child_schema,
                        root,
                        f"{path}/{key}",
                        ref_stack,
                    )
                )
        extras = set(instance) - set(property_schemas)
        additional = schema.get("additionalProperties")
        if additional is False and extras:
            errors.append(f"{path}:ADDITIONAL")
        elif isinstance(additional, Mapping):
            for key in extras:
                errors.extend(
                    _validate(
                        instance[key],
                        additional,
                        root,
                        f"{path}/{key}",
                        ref_stack,
                    )
                )
    if isinstance(instance, list):
        if _is_integer(schema.get("minItems")) and len(instance) < schema["minItems"]:
            errors.append(f"{path}:MIN_ITEMS")
        if _is_integer(schema.get("maxItems")) and len(instance) > schema["maxItems"]:
            errors.append(f"{path}:MAX_ITEMS")
        if schema.get("uniqueItems") is True and any(
            _json_equal(instance[left], instance[right])
            for left in range(len(instance))
            for right in range(left + 1, len(instance))
        ):
            errors.append(f"{path}:UNIQUE")
        prefix = schema.get("prefixItems")
        prefix_items = prefix if isinstance(prefix, list) else []
        for index, child_schema in enumerate(prefix_items[: len(instance)]):
            errors.extend(
                _validate(
                    instance[index],
                    child_schema,
                    root,
                    f"{path}/{index}",
                    ref_stack,
                )
            )
        items = schema.get("items")
        if isinstance(items, (Mapping, bool)):
            for index in range(len(prefix_items), len(instance)):
                errors.extend(
                    _validate(
                        instance[index],
                        items,
                        root,
                        f"{path}/{index}",
                        ref_stack,
                    )
                )
    all_of = schema.get("allOf")
    if isinstance(all_of, list):
        for branch in all_of:
            errors.extend(_validate(instance, branch, root, path, ref_stack))
    one_of = schema.get("oneOf")
    if isinstance(one_of, list):
        matches = sum(
            not _validate(instance, branch, root, path, ref_stack)
            for branch in one_of
        )
        if matches != 1:
            errors.append(f"{path}:ONE_OF")
    condition = schema.get("if")
    if isinstance(condition, (Mapping, bool)):
        condition_matches = not _validate(instance, condition, root, path, ref_stack)
        selected = schema.get("then" if condition_matches else "else")
        if isinstance(selected, (Mapping, bool)):
            errors.extend(_validate(instance, selected, root, path, ref_stack))
    return errors


def validate_instance(instance: Any, schema: Any) -> list[str]:
    _check_json_value(instance)
    root = check_schema(schema)
    return _validate(instance, root, root, "$", ())


def main() -> int:
    try:
        schema = loads_json(SCHEMA_PATH.read_bytes())
        state = loads_json(STATE_PATH.read_bytes())
        errors = validate_instance(state, schema)
    except (
        OSError,
        UnicodeError,
        json.JSONDecodeError,
        DuplicateKeyError,
        SchemaSubsetError,
    ):
        print(f"goal-control {SUBSET_PROTOCOL} validation failed")
        return 1
    if errors:
        print(f"goal-control {SUBSET_PROTOCOL} validation failed")
        return 1
    print(f"goal-control {SUBSET_PROTOCOL} validation passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
