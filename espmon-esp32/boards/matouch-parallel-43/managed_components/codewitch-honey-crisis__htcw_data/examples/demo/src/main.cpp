#include <stdio.h>
#include <htcw_data.hpp>
using namespace data;
int hash(const int& x) {
    return x;
}
int main(int argc, char** argv) {
    simple_vector<int> v;
    v.push_back(1);
    v.push_back(2);
    v.push_back(3);
    printf("v.begin()[0]=%d\r\n",v.begin()[0]);
    printf("v.begin()[1]=%d\r\n",v.begin()[1]);
    printf("v.begin()[2]=%d\r\n",v.begin()[2]);
    simple_fixed_map<int,int,2> test(hash);
    test.insert({1,3});
    test.insert({2,2});
    test.insert({3,1});
    printf("test.find(1)=%d\r\n",*test.find(1));
    printf("test.find(2)=%d\r\n",*test.find(2));
    printf("test.find(3)=%d\r\n",*test.find(3));
    if(test.find(4)==nullptr) {
        printf("test.find(4)=nullptr\r\n");
    }
    return 0;
}