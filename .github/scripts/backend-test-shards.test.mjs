import { test } from 'node:test';
import assert from 'node:assert/strict';
import { classTimings, parseClassList, parseFullListing, parseXunitXml, plan, verify } from './backend-test-shards.mjs';

const listing = `
Assembly: TemplateApp.Tests
  - Display name: "A.FooTests.One"
    Test method:  A.FooTests.One
  - Display name: "A.FooTests.Theory(value: 1)"
    Test method:  A.FooTests.Theory
  - Display name: "A.FooTests.Theory(value: 2)"
    Test method:  A.FooTests.Theory
  - Display name: "A.BarTests.Two"
    Test method:  A.BarTests.Two
`;

function xml(tests, collections) {
  const body = tests
    .map(([type, method, result = 'Pass']) => `<test name="${type}.${method}" type="${type}" method="${method}" result="${result}" time="1.5" />`)
    .join('');
  const heads = collections
    .map(([name, time]) => `<collection name="Test collection for ${name} (id: abc)" time="${time}">`)
    .join('');
  return `<assemblies><assembly>${heads}${body}</assembly></assemblies>`;
}

test('la lista de clases ignora el resumen que imprime el runner', () => {
  const text = 'A.FooTests\r\nA.Nested.BarTests\r\n\r\nResumen de la serie de pruebas: No se ejecutaron pruebas\r\n  total: 0\r\n';
  assert.deepEqual(parseClassList(text), ['A.FooTests', 'A.Nested.BarTests']);
});

test('la lista completa cuenta cada fila de una teoría', () => {
  assert.deepEqual([...parseFullListing(listing)], [['A.FooTests.One', 1], ['A.FooTests.Theory', 2], ['A.BarTests.Two', 1]]);
});

test('el plan pone cada clase en un solo shard y equilibra los segundos de cada uno', () => {
  const shards = plan(['A', 'B', 'C', 'D'], { A: 100, B: 60, C: 50, D: 10 }, 2);
  assert.deepEqual(shards.map((shard) => [shard.filter, shard.seconds]), [
    ['--filter-class A --filter-class D', 110],
    ['--filter-class B --filter-class C', 110],
  ]);
});

test('una clase sin tiempo registrado pesa la mediana de las conocidas', () => {
  const shards = plan(['A', 'B', 'C', 'Nueva'], { A: 30, B: 20, C: 10 }, 3);
  assert.deepEqual(shards.map((shard) => shard.seconds), [30, 30, 20]);
});

test('no arma shards vacíos si hay más shards que clases', () => {
  assert.equal(plan(['A'], {}, 4).length, 1);
});

test('verify pasa cuando cada test de la lista corrió una sola vez entre todos los shards', () => {
  const results = [
    parseXunitXml(xml([['A.FooTests', 'One'], ['A.FooTests', 'Theory'], ['A.FooTests', 'Theory']], [['A.FooTests', 3]])),
    parseXunitXml(xml([['A.BarTests', 'Two']], [['A.BarTests', 1.5]])),
  ];
  assert.deepEqual(verify(parseFullListing(listing), results), { listed: 4, total: 4, problems: [] });
});

test('verify falla si un shard no corrió una clase', () => {
  const results = [parseXunitXml(xml([['A.FooTests', 'One'], ['A.FooTests', 'Theory'], ['A.FooTests', 'Theory']], []))];
  assert.deepEqual(verify(parseFullListing(listing), results).problems, ['no corrió: A.BarTests.Two (0 de 1)']);
});

test('verify falla si una clase corrió en dos shards', () => {
  const results = [
    parseXunitXml(xml([['A.FooTests', 'One'], ['A.FooTests', 'Theory'], ['A.FooTests', 'Theory'], ['A.BarTests', 'Two']], [])),
    parseXunitXml(xml([['A.BarTests', 'Two']], [])),
  ];
  assert.deepEqual(verify(parseFullListing(listing), results).problems, ['corrió de más: A.BarTests.Two (2 de 1)']);
});

test('verify falla si un test no pasó o no estaba en la lista', () => {
  const results = [
    parseXunitXml(xml([['A.FooTests', 'One', 'Fail'], ['A.FooTests', 'Theory'], ['A.FooTests', 'Theory'], ['A.BarTests', 'Two'], ['A.OtraTests', 'Tres']], [])),
  ];
  assert.deepEqual(verify(parseFullListing(listing), results).problems, [
    'corrió sin estar en la lista: A.OtraTests.Tres',
    'no pasó: A.FooTests.One: Fail',
  ]);
});

test('los tiempos por clase salen de la colección de cada clase', () => {
  const results = [parseXunitXml(xml([], [['A.FooTests', 3.14], ['A.BarTests', 61]]))];
  assert.deepEqual(classTimings(results), { 'A.BarTests': 61, 'A.FooTests': 3.1 });
});
