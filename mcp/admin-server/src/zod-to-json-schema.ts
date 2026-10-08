import { z } from 'zod';

/**
 * Minimal zod → JSON Schema converter for the shapes this MCP uses.
 * Supports: object, string (uuid/url/email), number, integer, boolean, enum,
 * literal, array, optional, nullable. Everything else falls back to {} (any).
 */
export function zodToJsonSchema(schema: z.ZodTypeAny): Record<string, unknown> {
  const def: any = (schema as any)._def;
  const typeName: string = def.typeName;

  switch (typeName) {
    case 'ZodObject': {
      const shape = (schema as z.ZodObject<z.ZodRawShape>).shape;
      const properties: Record<string, unknown> = {};
      const required: string[] = [];
      for (const [key, value] of Object.entries(shape)) {
        properties[key] = zodToJsonSchema(value as z.ZodTypeAny);
        if (!isOptional(value as z.ZodTypeAny)) required.push(key);
      }
      const result: Record<string, unknown> = { type: 'object', properties };
      if (required.length > 0) result.required = required;
      result.additionalProperties = def.unknownKeys === 'strict' ? false : true;
      return result;
    }
    case 'ZodString': {
      const s: Record<string, unknown> = { type: 'string' };
      for (const check of def.checks ?? []) {
        if (check.kind === 'uuid') s.format = 'uuid';
        else if (check.kind === 'url') s.format = 'uri';
        else if (check.kind === 'email') s.format = 'email';
        else if (check.kind === 'min') s.minLength = check.value;
        else if (check.kind === 'max') s.maxLength = check.value;
      }
      return s;
    }
    case 'ZodNumber': {
      const n: Record<string, unknown> = { type: 'number' };
      for (const check of def.checks ?? []) {
        if (check.kind === 'int') n.type = 'integer';
        else if (check.kind === 'min') n.minimum = check.value;
        else if (check.kind === 'max') n.maximum = check.value;
      }
      return n;
    }
    case 'ZodBoolean':
      return { type: 'boolean' };
    case 'ZodLiteral':
      return { const: def.value };
    case 'ZodEnum':
      return { type: 'string', enum: def.values };
    case 'ZodNativeEnum':
      return { type: 'string', enum: Object.values(def.values) };
    case 'ZodArray':
      return { type: 'array', items: zodToJsonSchema(def.type) };
    case 'ZodOptional':
    case 'ZodNullable':
      return zodToJsonSchema(def.innerType);
    case 'ZodDefault':
      return { ...zodToJsonSchema(def.innerType), default: def.defaultValue() };
    case 'ZodUnion':
      return { anyOf: (def.options as z.ZodTypeAny[]).map(zodToJsonSchema) };
    case 'ZodRecord':
      return { type: 'object', additionalProperties: zodToJsonSchema(def.valueType) };
    case 'ZodAny':
    case 'ZodUnknown':
      return {};
    default:
      return {};
  }
}

function isOptional(schema: z.ZodTypeAny): boolean {
  const def: any = (schema as any)._def;
  if (def.typeName === 'ZodOptional' || def.typeName === 'ZodDefault') return true;
  if (def.typeName === 'ZodNullable') return isOptional(def.innerType);
  return false;
}
