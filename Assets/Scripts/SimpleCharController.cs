using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SimpleCharController : MonoBehaviour{

    [SerializeField] private bool bDebug = false;

    [SerializeField] private Rigidbody rbRef;

    [SerializeField] private Vector3 direction = Vector3.zero;
    [SerializeField] private float force = 100;

    // Update is called once per frame
    void Update(){
        HandleInput();
    }

    private void HandleInput() {
        //Redundant contingency much?
        if (!rbRef) {
            if (this.gameObject.GetComponent<Rigidbody>()) {
                rbRef = this.gameObject.GetComponent<Rigidbody>();
            }
            if (!rbRef) {
                this.gameObject.AddComponent<Rigidbody>();
                rbRef = this.gameObject.GetComponent<Rigidbody>();
            }
            if (!rbRef) {
                Debug.LogError("[Error] Missing rigidbody reference! Aborting operation...");
                return;
            }
        }

        Vector3 newVelocity;
        int up = 0;
        int down = 0;
        int left = 0;
        int right = 0;

        if (Input.GetKey(KeyCode.W)) {
            up = 1;
        }
        else {
            up = 0;
        }

        if (Input.GetKey(KeyCode.A)) {
            left = -1;
        }
        else {
            left = 0;
        }

        if (Input.GetKey(KeyCode.S)) {
            down = -1;
        }
        else {
            down = 0;
        }

        if (Input.GetKey(KeyCode.D)) {
            right = 1;
        }
        else {
            right = 0;
        }

        //Only modify velocity during player input
        if ( (left + right) != 0 || (up + down) != 0) {
            direction = new Vector3(left + right,0,up + down);
            rbRef.velocity = direction * force * Time.deltaTime;
        }
        
    }


}
