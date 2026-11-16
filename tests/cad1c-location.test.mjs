import {test} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
const source=fs.readFileSync(new URL('../backend/src/RealEstate.Web/wwwroot/address-editor.js',import.meta.url),'utf8');
const editor=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));
test('No coordinates remain absent, never a fabricated zero point',()=>{
    assert.equal(typeof editor.coordinateState,'function');
    assert.equal(editor.coordinateState({Latitude:'',Longitude:''}),'empty');
});
test('Only an explicitly complete pair can represent a point',()=>{
    assert.equal(editor.coordinateState({Latitude:'-23.55052',Longitude:'-46.633308'}),'ready');
    assert.equal(editor.coordinateState({Latitude:'0',Longitude:'0'}),'ready');
});
test('Partial coordinates stay distinguishable from missing coordinates',()=>{
    assert.equal(editor.coordinateState({Latitude:'-23',Longitude:''}),'partial');
    assert.equal(editor.coordinateState({Latitude:'',Longitude:'0'}),'partial');
});
test('Out of range or nonnumeric coordinates cannot be previewed as a valid point',()=>{
    for(const point of [{Latitude:'91',Longitude:'0'},{Latitude:'0',Longitude:'-181'},{Latitude:'abc',Longitude:'0'}])
        assert.equal(editor.coordinateState(point),'invalid');
});
