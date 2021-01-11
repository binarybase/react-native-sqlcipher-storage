/*
 * sqlite.js
 *
 * Created by Andrzej Porebski on 10/29/15.
 * Copyright (c) 2015 Andrzej Porebski.
 *
 * This library is available under the terms of the MIT License (2008).
 * See http://opensource.org/licenses/alphabetical for full text.
 */

import plugin, { SQLiteFactory } from "./sqlite.core.js";

var config = [
  [false, "SQLitePlugin", "transaction", false, true],
  [false, "SQLitePlugin", "readTransaction", false, true],
  [false, "SQLitePlugin", "close", false, false],
  [false, "SQLitePlugin", "executeSql", true, false],
  [false, "SQLitePluginTransaction", "executeSql", true, false],
  [false, "SQLiteFactory", "deleteDatabase", false, false],
  [true, "SQLiteFactory", "openDatabase", false, false]
];

var originalFns = {};
config.forEach(entry => {
  let [returnValueExpected, prototype, fn] = entry;
  let originalFn = plugin[prototype].prototype[fn];
  originalFns[prototype + "." + fn] = originalFn;
});

function enablePromiseRuntime(enable) {
  if (enable) {
    createPromiseRuntime();
  } else {
    createCallbackRuntime();
  }
}

function createCallbackRuntime() {
  config.forEach(entry => {
    let [
      returnValueExpected,
      prototype,
      fn,
      argsNeedPadding,
      reverseCallbacks
    ] = entry;
    plugin[prototype].prototype[fn] = originalFns[prototype + "." + fn];
  });
}

function createPromiseRuntime() {
  config.forEach(entry => {
    let [
      returnValueExpected,
      prototype,
      fn,
      argsNeedPadding,
      reverseCallbacks
    ] = entry;
    let originalFn = plugin[prototype].prototype[fn];
    plugin[prototype].prototype[fn] = function(...args) {
      if (argsNeedPadding && args.length == 1) {
        args.push([]);
      }
      let retValue;
      var promise = new Promise(
        function(resolve, reject) {
          let success = function(...args) {
            return returnValueExpected ? resolve(retValue) : resolve(args);
          };
          let error = function(err) {
            reject(err);
            return false;
          };
          retValue = originalFn.call(
            this,
            ...args,
            reverseCallbacks ? error : success,
            reverseCallbacks ? success : error
          );
        }.bind(this)
      );

      return promise;
    };
  });
}

SQLiteFactory.prototype.enablePromise = enablePromiseRuntime;

module.exports = new SQLiteFactory();
