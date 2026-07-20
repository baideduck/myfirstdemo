import re
with open(r'C:\unity\unity program\demo1\Assets\3c\C#sp\enemy_C#\AttackState\ThrustSlash.cs', 'r', encoding='utf-8') as f:
    content = f.read()

pattern = r'\s*private System\.Collections\.IEnumerator DelayedStart\(\)\s*\{(?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*\}'
content = re.sub(pattern, '', content, count=1)

with open(r'C:\unity\unity program\demo1\Assets\3c\C#sp\enemy_C#\AttackState\ThrustSlash.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print('Done')
