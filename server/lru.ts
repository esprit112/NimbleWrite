// A small least-recently-used map: reading or writing a key makes it the newest; past `max` the oldest goes.
// (A Map keeps insertion order, so deleting and re-inserting moves a key to the end.)
export class Lru<V> {
  private readonly map = new Map<string, V>();

  constructor(private readonly max: number) {}

  get(key: string): V | undefined {
    const v = this.map.get(key);
    if (v === undefined) return undefined;
    this.map.delete(key);
    this.map.set(key, v);
    return v;
  }

  set(key: string, value: V): void {
    this.map.delete(key);
    this.map.set(key, value);
    while (this.map.size > this.max) this.map.delete(this.map.keys().next().value as string);
  }

  get size(): number {
    return this.map.size;
  }

  clear(): void {
    this.map.clear();
  }
}
