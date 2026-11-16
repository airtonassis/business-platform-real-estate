// Generic evaluator only. Business predicates come from the versioned server descriptors.
export function evaluateExpression(rule,value){
    if(!rule)return false;
    if(rule.all!==null&&rule.all!==undefined)return rule.all.every(r=>evaluateExpression(r,value));
    if(rule.any!==null&&rule.any!==undefined)return rule.any.some(r=>evaluateExpression(r,value));
    if(rule.not)return !evaluateExpression(rule.not,value);
    return rule.values?.includes(value(rule.field)??'')??false;
}
export const applicable=(rules,condition,value)=>evaluateExpression(rules.conditions[condition],value);
