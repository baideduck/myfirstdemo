$file = Join-Path $PSScriptRoot "MeeleFighter.cs"
$content = [System.IO.File]::ReadAllText($file)

$oldBlock = @"
        animator.SetTrigger("BlockFail");

        // �л�����������ʾ�������ʱ�õ�������������
        if (CurrentCombatState == CombatState.Drawn)
            SwitchToRightWeapon();

        InAction = false;
        lastBlockEndTime = Time.time;
"@

$newBlock = @"
        // ���ü�����ײ�䣬��ֹ Block_Fail �ڼ����� Boss
        if (activeWeaponCollider)
            activeWeaponCollider.enabled = false;

        // �ر� Root Motion����ֹ�в���������ƶ�
        animator.applyRootMotion = false;

        animator.SetTrigger("BlockFail");

        // �л��һ������
        if (CurrentCombatState == CombatState.Drawn)
            SwitchToRightWeapon();

        // InAction �ɵ��÷�(OnBlockFailed)����
        lastBlockEndTime = Time.time;
"@

if ($content.Contains($oldBlock)) {
    $content = $content.Replace($oldBlock, $newBlock)
    [System.IO.File]::WriteAllText($file, $content)
    Write-Host "Replacement successful!"
} else {
    Write-Host "Old block not found - checking with substring search..."
    if ($content.Contains("InAction = false;")) {
        Write-Host "Found InAction = false; in file"
    }
    if ($content.Contains("animator.SetTrigger")) {
        Write-Host "Found animator.SetTrigger in file"
    }
}
