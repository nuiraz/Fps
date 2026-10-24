export const PBKDF2_ITERATIONS = 100_000;

const text = new TextEncoder();

export function bytesToB64(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary);
}

export function b64ToBytes(value: string): Uint8Array {
  const binary = atob(value);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}

export function bytesToB64Url(bytes: Uint8Array): string {
  return bytesToB64(bytes).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
}

export function b64UrlToBytes(value: string): Uint8Array {
  const padded = value.replaceAll("-", "+").replaceAll("_", "/") + "===".slice((value.length + 3) % 4);
  return b64ToBytes(padded);
}

export async function pbkdf2(password: string, salt: Uint8Array, iterations: number): Promise<Uint8Array> {
  const key = await crypto.subtle.importKey("raw", text.encode(password), "PBKDF2", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits(
    { name: "PBKDF2", hash: "SHA-256", salt: salt as BufferSource, iterations },
    key,
    256,
  );
  return new Uint8Array(bits);
}

export async function hashPassword(password: string, iterations = PBKDF2_ITERATIONS): Promise<{
  hash: string;
  salt: string;
  iterations: number;
  algo: string;
}> {
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const hash = await pbkdf2(password, salt, iterations);
  return { hash: bytesToB64(hash), salt: bytesToB64(salt), iterations, algo: "PBKDF2-SHA256" };
}

export async function verifyPassword(password: string, hashB64: string, saltB64: string, iterations: number): Promise<boolean> {
  const actual = await pbkdf2(password, b64ToBytes(saltB64), iterations);
  return fixedEqual(actual, b64ToBytes(hashB64));
}

export async function dummyVerify(password: string): Promise<void> {
  await pbkdf2(password, new Uint8Array(16), PBKDF2_ITERATIONS);
}

export type JwtPayload = {
  sub: string;
  sid: string;
  username: string;
  exp: number;
  iat: number;
};

export async function signJwt(payload: JwtPayload, secret: string): Promise<string> {
  const header = bytesToB64Url(text.encode(JSON.stringify({ alg: "HS256", typ: "JWT" })));
  const body = bytesToB64Url(text.encode(JSON.stringify(payload)));
  const signature = await hmac(`${header}.${body}`, secret);
  return `${header}.${body}.${bytesToB64Url(signature)}`;
}

export async function verifyJwt(token: string, secret: string): Promise<JwtPayload | null> {
  const parts = token.split(".");
  if (parts.length !== 3) return null;
  const [headerPart, bodyPart, signaturePart] = parts;
  if (!headerPart || !bodyPart || !signaturePart) return null;
  let header: { alg?: string };
  try {
    header = JSON.parse(new TextDecoder().decode(b64UrlToBytes(headerPart))) as { alg?: string };
  } catch {
    return null;
  }
  if (header.alg !== "HS256") return null;
  const expected = await hmac(`${headerPart}.${bodyPart}`, secret);
  const actual = b64UrlToBytes(signaturePart);
  if (!(await fixedEqual(expected, actual))) return null;
  try {
    const payload = JSON.parse(new TextDecoder().decode(b64UrlToBytes(bodyPart))) as JwtPayload;
    if (!payload.sub || !payload.sid || !payload.username || !payload.exp) return null;
    if (payload.exp * 1000 <= Date.now()) return null;
    return payload;
  } catch {
    return null;
  }
}

async function hmac(value: string, secret: string): Promise<Uint8Array> {
  const key = await crypto.subtle.importKey("raw", text.encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const signed = await crypto.subtle.sign("HMAC", key, text.encode(value));
  return new Uint8Array(signed);
}

export async function fixedEqual(a: Uint8Array, b: Uint8Array): Promise<boolean> {
  const [leftDigest, rightDigest] = await Promise.all([
    crypto.subtle.digest("SHA-256", a as BufferSource),
    crypto.subtle.digest("SHA-256", b as BufferSource),
  ]);
  const left = new Uint8Array(leftDigest);
  const right = new Uint8Array(rightDigest);
  const subtle = crypto.subtle as SubtleCrypto & {
    timingSafeEqual?: (x: BufferSource, y: BufferSource) => boolean;
  };
  if (typeof subtle.timingSafeEqual === "function") return subtle.timingSafeEqual(left, right);
  let diff = 0;
  for (let i = 0; i < left.length; i++) diff |= (left[i] ?? 0) ^ (right[i] ?? 0);
  return diff === 0;
}
