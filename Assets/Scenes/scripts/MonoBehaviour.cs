using UnityEngine; // ① Unity 기능 사용 선언
public class MyScript : MonoBehaviour // ② 파일 이름 = 클래스 이름
{ // ③ 중괄호로 범위 지정
public float speed = 5f; // ④ 변수: 자료형 이름 = 값;
private int count; // 세미콜론 (;)으로 종료
void Start() // ⑤ 함수: 시작할 때 한 번
{
Debug.Log("시작!"); // ⑥ Console에 글자 출력
}
void Update() // 매 프레임 한 번
{
count = count + 1;
Debug.Log(count);
if (count >= 10)
{
Debug.Log("10이 되었습니다.");
}
}
}