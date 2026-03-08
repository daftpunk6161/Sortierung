@{
    IncludeRules = @(
        'PSUseDeclaredVarsMoreThanAssignments',
        'PSUseApprovedVerbs',
        'PSAvoidUsingInvokeExpression',
        'PSAvoidGlobalVars'
    )
    ExcludeRules = @(
        'PSAvoidUsingPlainTextForPassword',
        'PSAvoidUsingWriteHost',
        'PSUseShouldProcessForStateChangingFunctions'
    )
}
