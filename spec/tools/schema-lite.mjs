// Dependency-free validator for the JSON Schema subset used by spec/schemas:
// type, enum, pattern, minLength/maxLength (Unicode code points), minimum, required, properties, oneOf and the
// uuid / date-time formats. Returns a list of human-readable errors (empty when valid).
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_TIME = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/i;
const typeOf = v => v === null ? 'null' : Array.isArray(v) ? 'array' : Number.isInteger(v) ? 'integer' : typeof v;
const matches = (type, value) => type === 'number' ? typeof value === 'number' : typeOf(value) === type;

export function validate(schema, value, path = '$') {
  const errors = [];
  if (schema.type) {
    const types = [].concat(schema.type);
    if (!types.some(t => matches(t, value))) return [`${path}: expected ${types.join('|')}, got ${typeOf(value)}`];
  }
  if (schema.enum && !schema.enum.includes(value)) errors.push(`${path}: ${JSON.stringify(value)} is not one of ${JSON.stringify(schema.enum)}`);
  if (typeof value === 'string') {
    const length = [...value].length;
    if (schema.minLength !== undefined && length < schema.minLength) errors.push(`${path}: shorter than ${schema.minLength}`);
    if (schema.maxLength !== undefined && length > schema.maxLength) errors.push(`${path}: longer than ${schema.maxLength}`);
    if (schema.pattern && !new RegExp(schema.pattern, 'u').test(value)) errors.push(`${path}: does not match ${schema.pattern}`);
    if (schema.format === 'uuid' && !UUID.test(value)) errors.push(`${path}: not a UUID`);
    if (schema.format === 'date-time' && (!DATE_TIME.test(value) || Number.isNaN(Date.parse(value)))) errors.push(`${path}: not an RFC 3339 date-time`);
  }
  if (typeof value === 'number' && schema.minimum !== undefined && value < schema.minimum) errors.push(`${path}: below ${schema.minimum}`);
  if (value && typeof value === 'object' && !Array.isArray(value)) {
    for (const name of schema.required ?? []) if (!(name in value)) errors.push(`${path}: missing required property ${name}`);
    for (const [name, sub] of Object.entries(schema.properties ?? {})) if (name in value) errors.push(...validate(sub, value[name], `${path}.${name}`));
  }
  if (schema.oneOf) {
    const passing = schema.oneOf.filter(branch => validate({ ...branch, type: undefined }, value, path).length === 0);
    if (passing.length !== 1) errors.push(`${path}: matches ${passing.length} of ${schema.oneOf.length} oneOf branches`);
  }
  return errors;
}
